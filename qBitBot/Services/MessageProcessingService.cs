using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using GenerativeAI.Classes;
using qBitBot.Models;
using qBitBot.Utilities;

namespace qBitBot.Services;

public class MessageProcessingService(
    ILogger<MessageProcessingService> logger,
    GoogleAiService googleAiService,
    Configuration config,
    HttpClient httpClient)
{
    private readonly ConcurrentDictionary<ulong, List<DateTimeOffset>> _recentUsage = [];
    private readonly ConcurrentDictionary<UserContext, List<PromptContentBase>> _openQuestions = [];

    public void AddQuestion(ulong userId, string userName, SocketUserMessage socketUserMessage, List<PromptContentBase> prompts)
    {
        var messages = _openQuestions.FirstOrDefault(x => x.Key.UserId == userId);

        if (messages.Key is null)
        {
            var promptContentBases = AttachSystemPrompt(prompts);

            var author = new UserContext
            {
                UserName = userName,
                UserId = userId,
                SocketUserMessage = socketUserMessage,
                Created = TimeProvider.System.GetUtcNow(),
            };
            _openQuestions.TryAdd(author, promptContentBases);

            logger.LogDebug("Added {Author} with {@Prompts}", userName, promptContentBases);
            return;
        }

        foreach (var prompt in prompts)
        {
            messages.Value.Add(prompt);
        }

        logger.LogDebug("Updated {Author} with {@Prompts}", userName, prompts);
    }


    public void RemoveAuthor(ulong authorId)
    {
        var messages = _openQuestions.FirstOrDefault(x => x.Key.UserId == authorId);
        if (messages.Key is null) return;
        _openQuestions.TryRemove(messages.Key, out _);
        logger.LogDebug("Removed {Author}", authorId);
    }

    public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var questions = _openQuestions
                    .Where(x => x.Key.Created.Add(config.GeminiRespondAfter) < TimeProvider.System.GetUtcNow());

                foreach (var question in questions)
                {
                    logger.LogDebug("Handling question by {Author}", question.Key.UserName);
                    var response = await GenerateResponseAsync(question.Value, cancellationToken);

                    if (string.IsNullOrWhiteSpace(response))
                    {
                        logger.LogWarning("Received response was empty, discarding question. Response: {Response}", response);
                        RemoveAuthor(question.Key.UserId);
                        continue;
                    }

                    var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
                    if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
                    {
                        logger.LogWarning("Received response started with 'NO', discarding question. Response: {Response}", response);
                        RemoveAuthor(question.Key.UserId);
                        continue;
                    }

                    logger.LogDebug("Sending response to Discord...");
                    if (responseSplit.Length < 2)
                    {
                        logger.LogWarning("Received response less than 3 lines, discarding question. Response: {Response}", response);
                        RemoveAuthor(question.Key.UserId);
                        continue;
                    }

                    await question.Key.SocketUserMessage.ReplyAsync(string.Join("\n", responseSplit[1..]));
                    RemoveAuthor(question.Key.UserId);
                }

                Thread.Sleep(1_000);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Exception during message processing");
                _openQuestions.Clear(); // Remove all questions to avoid spamming the API in the event it fails prior to removal.
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

        var question = _openQuestions.FirstOrDefault(x => x.Key.UserId == replyAuthor.Id);
        if (question.Key is null) return false;

        logger.LogInformation("Removing {@ReplyAuthor}'s question as {@MessageAuthor} responded",
            new { replyAuthor.Id, replyAuthor.Username }, new { messageAuthor.Id, replyAuthor.Username });
        RemoveAuthor(replyAuthor.Id);
        return true;
    }

    public async Task<List<PromptContentBase>> CreatePromptParts(List<IMessage> userMessages)
    {
        List<PromptContentBase> prompts = [];

        foreach (var message in userMessages)
        {
            if (message.Attachments.Count is not 0)
            {
                foreach (var attachment in message.Attachments)
                {
                    if (!attachment.ContentType.StartsWith("image/")) continue;

                    var response = await httpClient.GetAsync(attachment.ProxyUrl);
                    if (!response.IsSuccessStatusCode) continue;

                    var imageBytes = await response.Content.ReadAsByteArrayAsync();

                    prompts.Add(new PromptImage
                    {
                        MessageId = message.Id,
                        Image = new FileObject(imageBytes, attachment.Filename)
                    });
                }
            }

            if (!string.IsNullOrWhiteSpace(message.Content))
            {
                prompts.Add(new PromptText
                {
                    MessageId = message.Id,
                    Text = message.Content
                });
            }
        }

        return prompts;
    }

    public static List<PromptContentBase> AttachSystemPrompt(List<PromptContentBase> prompts)
    {
        List<PromptContentBase> systemText =
        [
            new PromptText
            {
                MessageId = 0,
                Text = "=== SYSTEM TEXT START ===\n" +
                       "DO YOU THINK THE FOLLOWING IS A SUPPORT QUESTION RELATED TO qBitTorrent? IF SO, RESPOND WITH 'YES', " +
                       "AND CONTINUE WITH ANSWERING THE QUESTION AS A FRIENDLY ASSISTANT (IF THERE ARE SCREENSHOTS ATTACHED, ANALYSE THEM), " +
                       "ELSE RESPOND WITH 'NO' AND STOP RESPONDING!\n" +
                       "CONTEXT: ASSUMING THE QUESTION BELOW IS SUPPORT-RELATED, IT MAY INCLUDE SCREENSHOTS. " +
                       "IF IT INCLUDES A SCREENSHOT OF THE CLIENT, CHECK THE PEERS, AVAILABILITY, STATUS, ETC. AND USE THIS TO CONTEXTUALISE YOUR TROUBLESHOOTING.\n" +
                       "=== SYSTEM TEXT END ==="
            }
        ];

        return systemText.Concat(prompts).ToList();
    }

    public void ClearOldUsages()
    {
        var keysToRemove = _recentUsage
            .Where(kvp => kvp.Value.All(time => time < TimeProvider.System.GetUtcNow().AddDays(-1)))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            _recentUsage.TryRemove(key, out _);
        }
    }

    public void AddUsage(ulong userId)
    {
        _recentUsage.AddOrUpdate(userId, _ => [TimeProvider.System.GetUtcNow()], (_, times) =>
        {
            times.Add(TimeProvider.System.GetUtcNow());
            return times;
        });
    }

    public int GetUserUsageCount(ulong guildUserId)
    {
        return _recentUsage.Count(x => x.Key == guildUserId);
    }
}
