using System.Collections.Concurrent;
using System.Timers;
using Discord;
using Discord.WebSocket;
using GenerativeAI.Classes;
using qBitBot.Enums;
using qBitBot.Models;
using qBitBot.Utilities;
using Timer = System.Timers.Timer;

namespace qBitBot.Services;

public class MessageProcessingService(
    ILogger<MessageProcessingService> logger,
    GoogleAiService googleAiService,
    Configuration config,
    IHttpClientFactory httpClientFactory,
    DiscordSocketClient client) : IDisposable
{
    private readonly ConcurrentDictionary<ulong, ConversationContext> _conversationContexts = [];
    private readonly ConcurrentDictionary<ulong, int> _usageCounts = [];

    private readonly Timer _cleanUpTask = new()
    {
        AutoReset = true,
        Interval = TimeSpan.FromMinutes(5).TotalMilliseconds,
        Enabled = true
    };

    public void AddOrUpdateQuestion(SocketGuildUser user, List<IMessage> messages, bool respondImmediately,
        ConversationContext.MessageCompleteDelegate messageComplete)
    {
        foreach (var message in messages)
        {
            _conversationContexts.AddOrUpdate(user.Id, new ConversationContext(user, message, false, messageComplete,
                config.GeminiRespondAfter), (_, context) =>
            {
                // New question, so we update appropriately.
                context.Responded = false;

                // We need to respond immediately if they are replying to the bot.
                if (context.Questions.Last() is ConversationContext.SystemMessage) respondImmediately = true;
                context.OnMessageCompleted = messageComplete;

                // Add the latest question...
                context.Questions.Add(new ConversationContext.UserMessage(message));
                return context;
            });
        }

        var context = _conversationContexts.Single(x => x.Key == user.Id).Value;

        context.UpdateLastActive();

        if (respondImmediately) context.RespondAfter.Interval = 1;
        if (!context.TimerSubscribed)
        {
            context.RespondAfter.Elapsed += RespondAfterOnElapsed;
            context.TimerSubscribed = true;
        }

        if (!context.RespondAfter.Enabled) context.RespondAfter.Start();

        logger.LogDebug("Added or updated {User} with {Message}", user.Username, messages.Last());
        return;

        void RespondAfterOnElapsed(object? sender, ElapsedEventArgs args) => ProcessMessagesAsync(sender, args, context);
    }

    private void RemoveAuthor(ulong authorId)
    {
        var messages = _conversationContexts.FirstOrDefault(x => x.Key == authorId);
        _conversationContexts.TryRemove(messages.Key, out var context);
        context?.Dispose();
        logger.LogDebug("Removed {Author}", authorId);
    }

    private async void ProcessMessagesAsync(object? _, ElapsedEventArgs __, ConversationContext context)
    {
        try
        {
            if (context.Questions.Last() is not ConversationContext.UserMessage) return;
            if (context.Responded) return;

            logger.LogDebug("Handling question by {Author}", context.User.Username);
            var promptParts = await CreatePromptParts(context.Questions);
            var response = await GenerateResponseAsync(promptParts, CancellationToken.None);

            _usageCounts.AddOrUpdate(context.User.Id, 1, (_, i) => i + 1);

            if (string.IsNullOrWhiteSpace(response))
            {
                logger.LogWarning("Received response was empty, discarding question. Response: {Response}", response);
                await context.OnMessageCompleted(false, string.Empty);
                RemoveAuthor(context.User.Id);
                return;
            }

            // Gemini, for some reason, loves to double-space.
            response = response.Replace("  ", " ");

            var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
            if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
            {
                logger.LogWarning("Received response started with 'NO', discarding question. Response: {Response}", response);
                await context.OnMessageCompleted(false, string.Empty);
                RemoveAuthor(context.User.Id);
                return;
            }

            try
            {
                logger.LogDebug("Attempting to pass Gemini response to Discord: {Gemini}", response);
                await context.OnMessageCompleted(true, response);
            }
            catch (ArgumentException ae)
            {
                await context.OnMessageCompleted(true, "Error during Gemini response.");
                logger.LogError(ae, "Gemini responded, but no message?");
                return;
            }

            context.Questions.Add(new ConversationContext.SystemMessage(response)); // Keep system context.
            logger.LogDebug("Successfully responded to {User}", context.User.Username);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception during message processing");
        }
        finally
        {
            // If it fails during reply, we should ignore and just set the state.
            context.Responded = true;
        }
    }

    private async Task<string?> GenerateResponseAsync(List<PromptContentBase> questions, CancellationToken cancellationToken)
    {
        return (await googleAiService.GenerateResponseAsync(questions, cancellationToken)).Text();
    }

    public bool IsAnsweredQuestion(SocketUserMessage message)
    {
        if (message.Type is not MessageType.Reply) return false;

        var messageAuthorId = message.Author.Id;
        var replyAuthorId = message.ReferencedMessage.Author.Id;
        var botId = client.CurrentUser.Id;

        if (messageAuthorId == replyAuthorId) return false;

        var context = _conversationContexts.FirstOrDefault(x => x.Key == messageAuthorId);
        if (context.Value is null) return false;

        // If the bot HAS responded and the user ISN'T replying to the bot, set Responded to true and return true
        if (context.Value.Responded && context.Value.Questions.LastOrDefault() is ConversationContext.SystemMessage &&
            replyAuthorId != botId)
        {
            context.Value.Responded = true;
            return true;
        }

        // If another user has responded (not the bot)
        if (replyAuthorId == botId || replyAuthorId == messageAuthorId) return false;
        context.Value.Responded = true;
        return true;
    }

    public bool IsUsageCapMet(ulong guildUserId)
    {
        if (!_usageCounts.TryGetValue(guildUserId, out var count)) return false;
        return count >= 10;
    }

    public bool HasUserAskedQuestion(ulong guildUserId)
    {
        var result = _conversationContexts.FirstOrDefault(x => x.Key == guildUserId);
        return result.Value is not null && result.Value.Responded;
    }

    private void CleanUpConversations(object? sender, ElapsedEventArgs args)
    {
        var oldQuestions = _conversationContexts
            .Where(x => TimeProvider.System.GetUtcNow() - x.Value.LastActive > config.DeleteQuestionsAfter);

        foreach (var question in oldQuestions)
        {
            RemoveAuthor(question.Key);
            _usageCounts.TryRemove(question.Key, out _);
        }
    }

    private async Task<List<PromptContentBase>> CreatePromptParts(List<ConversationContext.Message> questions)
    {
        using var httpClient = httpClientFactory.CreateClient("qBitHttpClient");
        List<PromptContentBase> prompts = [];

        foreach (var question in questions)
        {
            switch (question)
            {
                case ConversationContext.SystemMessage systemQuestion:
                {
                    if (!string.IsNullOrWhiteSpace(systemQuestion.Message))
                    {
                        prompts.Add(new PromptContentBase.PromptText
                        {
                            Sender = SenderType.System,
                            MessageId = 0,
                            Text = systemQuestion.ToString()
                        });
                    }

                    break;
                }
                case ConversationContext.UserMessage userQuestion:
                {
                    foreach (var attachment in userQuestion.Message.Attachments)
                    {
                        if (!attachment.ContentType.StartsWith("image/")) continue;

                        var response = await httpClient.GetAsync(attachment.ProxyUrl);
                        if (!response.IsSuccessStatusCode) continue;

                        var imageBytes = await response.Content.ReadAsByteArrayAsync();

                        prompts.Add(new PromptContentBase.PromptImage
                        {
                            Sender = SenderType.User,
                            MessageId = userQuestion.Message.Id,
                            Image = new FileObject(imageBytes, attachment.Filename)
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(userQuestion.Message.Content))
                    {
                        prompts.Add(new PromptContentBase.PromptText
                        {
                            Sender = SenderType.User,
                            MessageId = userQuestion.Message.Id,
                            Text = userQuestion.ToString()
                        });
                    }

                    break;
                }
            }
        }

        return prompts;
    }

    public void Subscribe()
    {
        _cleanUpTask.Elapsed += CleanUpConversations;
    }

    public void Dispose()
    {
        _cleanUpTask.Dispose();
    }
}
