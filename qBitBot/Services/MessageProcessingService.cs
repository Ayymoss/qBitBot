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
    HttpClient httpClient) : IDisposable
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
        Func<bool, string, Task> callback)
    {
        foreach (var message in messages)
        {
            _conversationContexts.AddOrUpdate(user.Id, new ConversationContext(user, message, false, callback,
                config.GeminiRespondAfter), (_, context) =>
            {
                // New question, so we update appropriately.
                context.Responded = false;

                // We need to respond immediately if they are replying to the bot.
                if (context.Questions.Last() is ConversationContext.SystemMessage) respondImmediately = true;
                context.OnMessageComplete = callback;

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
                await context.OnMessageComplete(false, string.Empty);
                RemoveAuthor(context.User.Id);
                return;
            }

            var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
            if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
            {
                logger.LogWarning("Received response started with 'NO', discarding question. Response: {Response}", response);
                await context.OnMessageComplete(false, string.Empty);
                RemoveAuthor(context.User.Id);
                return;
            }

            logger.LogDebug("Sending response to Discord...");
            if (responseSplit.Length < 2)
            {
                logger.LogWarning("Received response less than 3 lines, discarding question. Response: {Response}", response);
                await context.OnMessageComplete(false, string.Empty);
                RemoveAuthor(context.User.Id);
                return;
            }

            var geminiResponse = string.Join("\n", responseSplit[1..]);
            await context.OnMessageComplete(true, geminiResponse);
            context.Questions.Add(new ConversationContext.SystemMessage(geminiResponse)); // Keep system context.
            logger.LogDebug("Successfully responded to {User}", context.User.Username);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception during message processing");
            // Not sure that this is needed if we use finally -> responded
            //_conversationContexts.Clear(); // Remove all questions to avoid spamming the API in the event it fails prior to removal.
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

    public bool IsAnsweredQuestion(IUser messageAuthor, IUser replyAuthor)
    {
        // User is the replier.
        if (messageAuthor.Id == replyAuthor.Id) return false;

        var context = _conversationContexts.FirstOrDefault(x => x.Key == replyAuthor.Id);
        if (context.Value is null) return false;

        // Another user responded to the message author.
        if (context.Key == messageAuthor.Id) return context.Value.Responded;

        context.Value.Responded = true;
        return context.Value.Responded;
    }

    public bool IsUsageCapMet(ulong guildUserId)
    {
        if (!_usageCounts.TryGetValue(guildUserId, out var count)) return false;
        return count > 3;
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
        httpClient.Dispose();
    }
}
