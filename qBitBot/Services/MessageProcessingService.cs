using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using GenerativeAI.Classes;
using qBitBot.Enums;
using qBitBot.Models;
using qBitBot.Utilities;

namespace qBitBot.Services;

public class MessageProcessingService(
    ILogger<MessageProcessingService> logger,
    GoogleAiService googleAiService,
    Configuration config,
    HttpClient httpClient)
{
    /*

Behaviour 1 - New User (Naturally invoked, no commands)
1. A new user joins. They ask a question. If no one else answers after Configuration.GeminiRespondAfter, the bot responds.
2. A user asks a follow-up question to the bot's response, the bot responds immediately, with the previous question and the bot's reply for added context.
3. A user isn't classed as a 'new user' after they've been in the Discord for more than Configuration.IgnoreQuestionsAfter,
    so the bot will not autoreply to anyone older than that duration.

Behaviour 2 - Adhoc Usage (Command-invoked)
1. A user asks a question and invokes the 'MessageCommand', the bot responds immediately.
2. A user asks a follow-up question to the bot's response, same as 'New User' behaviour, it responds immediately with the user's context and bot's previous reply for context.
3. If the user asks more than 3 questions in Configuration.UserContextClear, the bot will tell them they've hit the usage cap to prevent abuse.

     */


    private readonly ConcurrentDictionary<ulong, ConversationContext> _conversationContexts = [];

    public void AddOrUpdateQuestion(SocketGuildUser user, SocketUserMessage message, bool respondImmediately)
    {
        var context = _conversationContexts.AddOrUpdate(user.Id, new ConversationContext(user, message, false), (_, context) =>
        {
            // We need to set prior questions as 'Responded' so there isn't a flood or AI provided answers.
            var questions = context.Questions
                .Where(x => x is ConversationContext.UserQuestion { Responded: false })
                .Cast<ConversationContext.UserQuestion>();
            foreach (var question in questions) question.Responded = true;

            // We need to respond immediately if they are replying to the bot.
            if (context.Questions.Last() is ConversationContext.SystemQuestion) respondImmediately = true;

            // Add the latest question...
            context.Questions.Add(new ConversationContext.UserQuestion(message, respondImmediately));
            return context;
        });

        context.UpdateLastActive();
        logger.LogDebug("Added or updated {User} with {Message}", user.Username, message.Content);
    }

    public void RemoveAuthor(ulong authorId)
    {
        var messages = _conversationContexts.FirstOrDefault(x => x.Key == authorId);
        _conversationContexts.TryRemove(messages.Key, out _);
        logger.LogDebug("Removed {Author}", authorId);
    }


    public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var oldQuestions = _conversationContexts
                .Where(x => x.Value.LastActive < TimeProvider.System.GetUtcNow().Add(config.DeleteQuestionsAfter));
            foreach (var question in oldQuestions)
            {
                RemoveAuthor(question.Key);
            }

            try
            {
                var conversationContexts = _conversationContexts
                    .Where(x => x.Value.Questions.Any(q => q is ConversationContext.UserQuestion
                    {
                        Responded: false
                    })).Select(x => x.Value);

                foreach (var conversationContext in conversationContexts)
                {
                    if (conversationContext.Questions.Last() is not ConversationContext.UserQuestion userQuestion) continue;
                    if (!userQuestion.RespondImmediately &&
                        conversationContext.LastActive < TimeProvider.System.GetUtcNow().Add(config.GeminiRespondAfter)) continue;

                    logger.LogDebug("Handling question by {Author}", conversationContext.User.Username);
                    var promptParts = await CreatePromptParts(conversationContext.Questions);
                    var response = await GenerateResponseAsync(promptParts, cancellationToken);

                    if (string.IsNullOrWhiteSpace(response))
                    {
                        logger.LogWarning("Received response was empty, discarding question. Response: {Response}", response);
                        RemoveAuthor(conversationContext.User.Id);
                        continue;
                    }

                    var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
                    if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
                    {
                        logger.LogWarning("Received response started with 'NO', discarding question. Response: {Response}", response);
                        RemoveAuthor(conversationContext.User.Id);

                        continue;
                    }

                    logger.LogDebug("Sending response to Discord...");
                    if (responseSplit.Length < 2)
                    {
                        logger.LogWarning("Received response less than 3 lines, discarding question. Response: {Response}", response);
                        RemoveAuthor(conversationContext.User.Id);

                        continue;
                    }

                    userQuestion.Responded = true;
                    var geminiResponse = string.Join("\n", responseSplit[1..]);
                    await userQuestion.Message.ReplyAsync(geminiResponse);
                    conversationContext.Questions.Add(new ConversationContext.SystemQuestion(geminiResponse)); // Keep system context.
                }

                Thread.Sleep(1_000);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Exception during message processing");
                _conversationContexts.Clear(); // Remove all questions to avoid spamming the API in the event it fails prior to removal.
            }
        }
    }

    public async Task<string?> GenerateResponseAsync(List<PromptContentBase> questions, CancellationToken cancellationToken)
    {
        return (await googleAiService.GenerateResponseAsync(questions, cancellationToken)).Text();
    }

    public bool IsAnsweredQuestion(IUser messageAuthor, IUser replyAuthor)
    {
        if (messageAuthor.Id == replyAuthor.Id) return false;

        var context = _conversationContexts.FirstOrDefault(x => x.Key == replyAuthor.Id).Value;
        if (context.Questions.FirstOrDefault(x => x is ConversationContext.UserQuestion { Responded: false }) is not
            ConversationContext.UserQuestion question) return false;

        question.Responded = true;
        logger.LogInformation("Setting {@ReplyAuthor}'s question as {@MessageAuthor} responded",
            new { replyAuthor.Id, replyAuthor.Username }, new { messageAuthor.Id, replyAuthor.Username });
        return true;
    }

    public async Task<List<PromptContentBase>> CreatePromptParts(List<ConversationContext.Question> questions)
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
                            Sender = SenderType.User,
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

            break;
        }

        return AttachSystemPrompt(prompts);
    }

    public static List<PromptContentBase> AttachSystemPrompt(List<PromptContentBase> prompts)
    {
        var systemText = new PromptContentBase.PromptText
        {
            Sender = SenderType.System,
            MessageId = 0,
            Text = "=== SYSTEM TEXT START ===\n" +
                   "DO YOU THINK THE FOLLOWING IS A SUPPORT QUESTION RELATED TO qBitTorrent? IF SO, RESPOND WITH 'YES', " +
                   "AND CONTINUE WITH ANSWERING THE QUESTION AS A FRIENDLY ASSISTANT (IF THERE ARE SCREENSHOTS ATTACHED, ANALYSE THEM), " +
                   "ELSE RESPOND WITH 'NO' AND STOP RESPONDING!\n" +
                   "CONTEXT: ASSUMING THE QUESTION BELOW IS SUPPORT-RELATED, IT MAY INCLUDE SCREENSHOTS. " +
                   "IF IT INCLUDES A SCREENSHOT OF THE CLIENT, CHECK THE PEERS, AVAILABILITY, STATUS, ETC. AND USE THIS TO CONTEXTUALISE YOUR TROUBLESHOOTING.\n" +
                   "=== SYSTEM TEXT END ==="
        };

        return prompts.Prepend(systemText).ToList();
    }

    public void ClearOldQuestions()
    {
        var userIds = _conversationContexts
            .Where(kvp => kvp.Value.LastActive < TimeProvider.System.GetUtcNow().Add(config.DeleteQuestionsAfter))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var userId in userIds)
        {
            RemoveAuthor(userId);
        }
    }

    public bool IsUsageCapMet(ulong guildUserId)
    {
        return _conversationContexts.SingleOrDefault(x => x.Key == guildUserId).Value.UsageCapHit;
    }

    public void InvalidateQuestion(List<IMessage> relatedMessages)
    {
        var messageIds = relatedMessages.Select(m => m.Id).ToHashSet();

        foreach (var conversationKvp in _conversationContexts)
        {
            var context = conversationKvp.Value;
            foreach (var question in context.Questions)
            {
                if (question is not ConversationContext.UserQuestion userQuestion ||
                    !messageIds.Contains(userQuestion.Message.Id)) continue;

                userQuestion.Responded = true;
                logger.LogDebug("Setting question from {QuestionAuthor} as responded (MessageCommand used)", context.User);
            }
        }
    }
}
