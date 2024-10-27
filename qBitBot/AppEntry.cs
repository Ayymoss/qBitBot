using qBitBot.Services;

namespace qBitBot;

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

public class AppEntry(MessageProcessingService messageProcessingService, DiscordBotService discordBotService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        messageProcessingService.Subscribe();
        await discordBotService.StartBotAsync();
    }
}
