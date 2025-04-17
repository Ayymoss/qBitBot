using Microsoft.Extensions.Hosting;
using qBitJanitor.Services;

namespace qBitJanitor;

public class AppEntry(DiscordBotService discordBotService) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await discordBotService.StartBotAsync();
    }
}
