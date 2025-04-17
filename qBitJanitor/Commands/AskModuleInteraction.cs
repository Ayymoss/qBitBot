using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using qBitJanitor.Services;

namespace qBitJanitor.Commands;

public class AskModuleInteraction(MessageProcessingService messageProcessingService, ILogger<AskModuleInteraction> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [DefaultMemberPermissions(GuildPermission.SendMessages)]
    [MessageCommand("Check Question Integrity")]
    public async Task AskAsync(IMessage message)
    {
        if (message.Author is not SocketGuildUser guildUser) return;

        try
        {
            if (guildUser.Roles.All(role => role.Id == guildUser.Guild.EveryoneRole.Id) &&
                messageProcessingService.HasMetUsageCapacity(guildUser.Id))
            {
                await DeferAsync(ephemeral: true);

                await FollowupAsync(
                    "You've used this bot a lot recently. If you wish to continue, please use https://gemini.google.com/ yourself.",
                    ephemeral: true);

                logger.LogInformation("{User} has hit their usage cap", guildUser.Username);
                return;
            }

            await DeferAsync();

            var userMessagesBelow = await Context.Channel.GetMessagesAsync(message.Id, Direction.After, 10)
                .FlattenAsync();

            var userMessages = userMessagesBelow.Prepend(message);
            var relatedMessages = userMessages
                .Where(m => m.Author.Id == message.Author.Id)
                .Where(m => (m.Timestamp - message.Timestamp).Duration() <= TimeSpan.FromMinutes(10))
                .OrderBy(m => m.Timestamp)
                .ToList();

            await messageProcessingService.RespondToUserAsync(guildUser.Id, string.Join(" ", relatedMessages), async (_, response)
                => await FollowupAsync(response));
        }
        catch (Exception e)
        {
            await FollowupAsync("Error during response...", ephemeral: true);
            logger.LogError(e, "Error during AskAsync execution. Author {Author}, Question {Question}", message.Author.Username,
                message.Content);
        }
    }
}
