using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using qBitBot.Services;

namespace qBitBot.Commands;

public class AskModuleInteraction(MessageProcessingService messageProcessingService, ILogger<AskModuleInteraction> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    [DefaultMemberPermissions(GuildPermission.SendMessages)]
    [MessageCommand("Ask Gemini")]
    public async Task AskAsync(IMessage message)
    {
        if (message.Author is not SocketGuildUser guildUser) return;

        try
        {
            messageProcessingService.ClearOldQuestions();

            if (guildUser.Roles.All(role => role.Id == guildUser.Guild.EveryoneRole.Id) && messageProcessingService.IsUsageCapMet(guildUser.Id))
            {
                await FollowupAsync("You've used this bot a lot recently. If you wish to continue, " +
                                    "please use https://gemini.google.com/ yourself.", ephemeral: true);
                return;
            }

            await DeferAsync();

            var userMessagesAbove = await Context.Channel.GetMessagesAsync(message.Id, Direction.Before, 3)
                .FlattenAsync();
            var userMessagesBelow = await Context.Channel.GetMessagesAsync(message.Id, Direction.After, 10)
                .FlattenAsync();

            var userMessages = userMessagesAbove.Concat(userMessagesBelow).Prepend(message);
            var relatedMessages = userMessages
                .Where(m => m.Author.Id == message.Author.Id)
                .OrderBy(m => m.Timestamp)
                .ToList();

            var prompts = MessageProcessingService.AttachSystemPrompt(await messageProcessingService.CreatePromptParts(relatedMessages));
            var response = await messageProcessingService.GenerateResponseAsync(prompts, default);

            if (string.IsNullOrWhiteSpace(response))
            {
                await FollowupAsync("No response received...", ephemeral: true);
                return;
            }

            messageProcessingService.AddUsage(guildUser.Id);

            var responseSplit = response.Split(["\r\n", "\n"], StringSplitOptions.None);
            if (response.StartsWith("NO") || responseSplit.First().Contains("NO"))
            {
                await FollowupAsync("Question is not support related.", ephemeral: true);
                return;
            }

            if (responseSplit.Length < 2)
            {
                await FollowupAsync("No response received...", ephemeral: true);
                return;
            }

            messageProcessingService.InvalidateQuestion(relatedMessages);
            await FollowupAsync(string.Join("\n", responseSplit[1..]));
        }
        catch (Exception e)
        {
            await FollowupAsync("Error during response...", ephemeral: true);
            logger.LogError(e, "Error during AskAsync execution. Author {Author}, Question {Question}", message.Author.Username,
                message.Content);
        }
    }
}
