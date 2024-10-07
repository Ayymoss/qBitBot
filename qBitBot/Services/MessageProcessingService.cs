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
                    Text = "DO YOU THINK THE FOLLOWING IS A SUPPORT QUESTION? IF SO, RESPOND WITH 'YES', " +
                           "AND CONTINUE WITH ANSWERING THE QUESTION AS A FRIENDLY ASSISTANT, " +
                           "ELSE RESPOND WITH 'NO' AND STOP RESPONDING!"
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

            logger.LogInformation("Added {Author} with {@Prompts}", socketUser.Username, promptContentBases);
            return;
        }

        foreach (var prompt in prompts)
        {
            messages.Value.Add(prompt);
        }

        logger.LogInformation("Updated {Author} with {@Prompts}", socketUser.Username, prompts);
    }

    public void RemoveAuthor(ulong authorId)
    {
        var messages = _openQuestions.FirstOrDefault(x => x.Key.SocketUser.Id == authorId);
        if (messages.Key is null) return;
        _openQuestions.TryRemove(messages.Key, out _);
        logger.LogInformation("Removed {Author}", authorId);
    }

    public async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var questions = _openQuestions.Where(x => x.Key.Created.AddMinutes(-1) < TimeProvider.System.GetUtcNow());

                foreach (var question in questions)
                {
                    logger.LogInformation("Handling question by {Author}", question.Key.SocketUser.Username);
                    var response = (await googleAiService.GenerateResponseAsync(question.Value, cancellationToken)).Text();
                    logger.LogInformation("Received response from Gemini...");
                    if (string.IsNullOrWhiteSpace(response)) continue;

                    logger.LogInformation("Sending response to Discord...");
                    await question.Key.SocketUserMessage.ReplyAsync(response);
                    RemoveAuthor(question.Key.SocketUser.Id);
                }

                Thread.Sleep(1_000);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Exception during message processing");
            }
        }
    }
}
