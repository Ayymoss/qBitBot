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

    private readonly Timer _cleanUpTask = new()
    {
        AutoReset = true,
        Interval = TimeSpan.FromMinutes(5).TotalMilliseconds,
        Enabled = true
    };

    public void AddOrUpdateQuestion(SocketGuildUser user, List<IMessage> messages, bool respondImmediately, Func<string, Task> callback)
    {
        foreach (var message in messages)
        {
            _conversationContexts.AddOrUpdate(user.Id, new ConversationContext(user, message, false, callback,
                config.GeminiRespondAfter), (_, context) =>
            {
                // New question, so we update appropriately.
                context.Responded = false;

                // We need to respond immediately if they are replying to the bot.
                if (context.Questions.Last() is ConversationContext.SystemQuestion) respondImmediately = true;
                context.OnMessageComplete = callback;

                // Add the latest question...
                context.Questions.Add(new ConversationContext.UserQuestion(message));
                return context;
            });
        }

        var context = _conversationContexts.Single(x => x.Key == user.Id).Value;

        context.UpdateLastActive();

        if (respondImmediately) context.RespondTimer.Interval = 1;
        if (!context.TimerSubscribed)
        {
            context.RespondTimer.Elapsed += RespondTimerOnElapsed;
            context.TimerSubscribed = true;
        }

        context.RespondTimer.Start();

        logger.LogDebug("Added or updated {User} with {Message}", user.Username, messages.Last());
        return;

        void RespondTimerOnElapsed(object? sender, ElapsedEventArgs args) => ProcessMessagesAsync(sender, args, context);
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
            if (context.Questions.Last() is not ConversationContext.UserQuestion) return;
            if (context.Responded) return;

            logger.LogDebug("Handling question by {Author}", context.User.Username);
            var promptParts = await CreatePromptParts(context.Questions);
            var response = await GenerateResponseAsync(promptParts, CancellationToken.None);

            if (string.IsNullOrWhiteSpace(response))
            {
                logger.LogWarning("Received response was empty, discarding question. Response: {Response}", response);
                RemoveAuthor(context.User.Id);
                return;
            }

            var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
            if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
            {
                logger.LogWarning("Received response started with 'NO', discarding question. Response: {Response}", response);
                RemoveAuthor(context.User.Id);
                return;
            }

            logger.LogDebug("Sending response to Discord...");
            if (responseSplit.Length < 2)
            {
                logger.LogWarning("Received response less than 3 lines, discarding question. Response: {Response}", response);
                RemoveAuthor(context.User.Id);
                return;
            }

            var geminiResponse = string.Join("\n", responseSplit[1..]);
            await context.OnMessageComplete(geminiResponse);
            context.Responded = true;
            context.Questions.Add(new ConversationContext.SystemQuestion(geminiResponse)); // Keep system context.
        }
        catch (Exception e)
        {
            logger.LogError(e, "Exception during message processing");
            _conversationContexts.Clear(); // Remove all questions to avoid spamming the API in the event it fails prior to removal.
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

        var context = _conversationContexts.FirstOrDefault(x => x.Key == messageAuthor.Id);
        if (context.Value is null) return false;

        // Another user responded to the message author.
        if (context.Key != messageAuthor.Id) context.Value.Responded = true;

        return context.Value.Responded;
    }

    public void ClearOldQuestions()
    {
        var userIds = _conversationContexts
            .Where(kvp => TimeProvider.System.GetUtcNow() - kvp.Value.LastActive > config.DeleteQuestionsAfter)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var userId in userIds)
        {
            RemoveAuthor(userId);
        }
    }

    public bool IsUsageCapMet(ulong guildUserId)
    {
        var result = _conversationContexts.SingleOrDefault(x => x.Key == guildUserId);
        if (result.Value is null) return false;

        result.Value.UsageCapInformed = true;
        return result.Value.UsageCapHit;
    }

    public bool IsUserInformedOfCap(ulong guildUserId)
    {
        var result = _conversationContexts.SingleOrDefault(x => x.Key == guildUserId);
        return result.Value is not null && result.Value.UsageCapInformed;
    }

    public bool HasUserAskedQuestion(ulong guildUserId) => _conversationContexts.Any(x => x.Key == guildUserId);

    private void CleanUpConversations(object? sender, ElapsedEventArgs args)
    {
        var oldQuestions = _conversationContexts
            .Where(x => TimeProvider.System.GetUtcNow() - x.Value.LastActive > config.DeleteQuestionsAfter);

        foreach (var question in oldQuestions)
        {
            RemoveAuthor(question.Key);
        }
    }

    public void Subscribe()
    {
        _cleanUpTask.Elapsed += CleanUpConversations;
    }

    private async Task<List<PromptContentBase>> CreatePromptParts(List<ConversationContext.Question> questions)
    {
        List<PromptContentBase> prompts = [];

        foreach (var question in questions)
        {
            switch (question)
            {
                case ConversationContext.SystemQuestion systemQuestion:
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
                case ConversationContext.UserQuestion userQuestion:

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

        return prompts;
    }

    public void Dispose()
    {
        _cleanUpTask.Dispose();
        httpClient.Dispose();
    }
}
