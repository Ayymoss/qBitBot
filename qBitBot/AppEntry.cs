using qBitBot.Services;

namespace qBitBot;

public class AppEntry(MessageProcessingService messageProcessingService, DiscordBotService discordBotService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(async () => await messageProcessingService.ProcessMessagesAsync(cancellationToken), cancellationToken);
        await discordBotService.StartBotAsync();
        cancellationToken.WaitHandle.WaitOne();
    }
}
