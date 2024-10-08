using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using qBitBot.Models;

namespace qBitBot.Services;

public class MessageProcessingService(ILogger<MessageProcessingService> logger, GoogleAiService googleAiService)
{
    private readonly ConcurrentDictionary<UserContext, List<PromptContentBase>> _openQuestions = [];

    public void AddQuestion(SocketUser socketUser, SocketUserMessage socketUserMessage, List<PromptContentBase> prompts)
    {
        var messages = _openQuestions.FirstOrDefault(x => x.Key.SocketUser.Id == socketUser.Id);

        if (messages.Key is null)
        {
            List<PromptContentBase> systemText =
            [
                new PromptText
                {
                    MessageId = 0,
                    Text = "=== SYSTEM TEXT START ===\n" +
                           "DO YOU THINK THE FOLLOWING IS A SUPPORT QUESTION? IF SO, RESPOND WITH 'YES', " +
                           "AND CONTINUE WITH ANSWERING THE QUESTION AS A FRIENDLY ASSISTANT (IF THERE ARE SCREENSHOTS ATTACHED, ANALYSE THEM), " +
                           "ELSE RESPOND WITH 'NO' AND STOP RESPONDING!\n" +
                           "CONTEXT: ASSUMING THE QUESTION BELOW IS SUPPORT-RELATED, IT WILL LIKELY BE qBitTorrent FOCUSED. " +
                           "IF IT INCLUDES A SCREENSHOT OF THE CLIENT, CHECK THE PEERS, AVAILABILITY, STATUS, ETC. AND USE THIS TO CONTEXTUALISE YOUR TROUBLESHOOTING.\n" +
                           "=== SYSTEM TEXT END ==="
                }
            ];

            var promptContentBases = systemText.Concat(prompts).ToList();

            var author = new UserContext
            {
                SocketUser = socketUser,
                SocketUserMessage = socketUserMessage,
                Created = TimeProvider.System.GetUtcNow()
            };
            _openQuestions.TryAdd(author, promptContentBases);

            logger.LogDebug("Added {Author} with {@Prompts}", socketUser.Username, promptContentBases);
            return;
        }

        foreach (var prompt in prompts)
        {
            messages.Value.Add(prompt);
        }

        logger.LogDebug("Updated {Author} with {@Prompts}", socketUser.Username, prompts);
    }

    public void RemoveAuthor(ulong authorId)
    {
        var messages = _openQuestions.FirstOrDefault(x => x.Key.SocketUser.Id == authorId);
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
                var questions = _openQuestions.Where(x => x.Key.Created.AddMinutes(1) < TimeProvider.System.GetUtcNow());

                foreach (var question in questions)
                {
                    logger.LogDebug("Handling question by {Author}", question.Key.SocketUser.Username);
                    var response = (await googleAiService.GenerateResponseAsync(question.Value, cancellationToken)).Text();
                    logger.LogDebug("Received response from Gemini...");
                    if (string.IsNullOrWhiteSpace(response))
                    {
                        logger.LogWarning("Received response was empty, discarding question. Response: {Response}", response);
                        RemoveAuthor(question.Key.SocketUser.Id);
                        continue;
                    }

                    var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
                    if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
                    {
                        logger.LogWarning("Received response started with 'NO', discarding question. Response: {Response}", response);
                        RemoveAuthor(question.Key.SocketUser.Id);
                        continue;
                    }

                    logger.LogDebug("Sending response to Discord...");
                    if (responseSplit.Length < 2)
                    {
                        logger.LogWarning("Received response less than 3 lines, discarding question. Response: {Response}", response);
                        RemoveAuthor(question.Key.SocketUser.Id);
                        continue;
                    }

                    await question.Key.SocketUserMessage.ReplyAsync(string.Join("\n", responseSplit[1..]));
                    RemoveAuthor(question.Key.SocketUser.Id);
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

    public bool IsAnsweredQuestion(IUser messageAuthor, IUser replyAuthor)
    {
        if (messageAuthor.Id == replyAuthor.Id) return false;

        var question = _openQuestions.FirstOrDefault(x => x.Key.SocketUser.Id == replyAuthor.Id);
        if (question.Key is null) return false;

        logger.LogInformation("Removing {@ReplyAuthor}'s question as {@MessageAuthor} responded",
            new { replyAuthor.Id, replyAuthor.Username }, new { messageAuthor.Id, replyAuthor.Username });
        RemoveAuthor(replyAuthor.Id);
        return true;
    }
}
