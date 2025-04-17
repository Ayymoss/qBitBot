using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using qBitBot.Commands;
using qBitBot.Utilities;

namespace qBitBot.Services;

public class DiscordBotService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly Configuration _config;
    private readonly MessageProcessingService _messageProcessingService;
    private readonly IServiceProvider _serviceProvider;

    public DiscordBotService(DiscordSocketClient client, ILogger<DiscordBotService> logger, Configuration config,
        MessageProcessingService messageProcessingService, IServiceProvider serviceProvider)
    {
        _client = client;
        _logger = logger;
        _config = config;
        _serviceProvider = serviceProvider;
        _messageProcessingService = messageProcessingService;

        _client.Log += Log;
        _client.MessageReceived += MessageReceivedAsync;
    }

    public async Task StartBotAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _config.BotToken);
        await _client.SetStatusAsync(UserStatus.Online);

        var interactionService = new InteractionService(_client);
        await interactionService.AddModulesAsync(typeof(AskModuleInteraction).Assembly, _serviceProvider);

        _client.InteractionCreated += async interaction =>
        {
            var ctx = new SocketInteractionContext(_client, interaction);
            await interactionService.ExecuteCommandAsync(ctx, _serviceProvider);
        };

        _client.Ready += async () => await interactionService.RegisterCommandsGloballyAsync();

        await _client.StartAsync();
    }

    private Task MessageReceivedAsync(SocketMessage socketMessage)
    {
        if (socketMessage.Author.IsBot || socketMessage.Author.IsWebhook) return Task.CompletedTask;
        if (socketMessage is not SocketUserMessage message) return Task.CompletedTask;
        if (socketMessage.Author is not SocketGuildUser guildUser) return Task.CompletedTask;

        switch (message.Type)
        {
            case MessageType.Reply when message.ReferencedMessage.Author.Id != _client.CurrentUser.Id:
            {
                if (message.ReferencedMessage.Author.Id != message.Author.Id &&
                    _messageProcessingService.MarkConversationAsResponded(message.ReferencedMessage.Author.Id))
                {
                    _logger.LogDebug("{User} responded to {Asker}, setting as replied", guildUser.Username,
                        message.ReferencedMessage.Author.Username);
                }
                else
                {
                    _logger.LogDebug("Ignoring reply by {User} as not related to any current bot conversation", guildUser.Username);
                }

                return Task.CompletedTask;
            }
            // Respond ONLY if the user is replying to the bot *directly*.
            case MessageType.Reply when message.ReferencedMessage.Author.Id == _client.CurrentUser.Id:
            {
                if (_messageProcessingService.IsUserInConversation(guildUser.Id))
                {
                    _messageProcessingService.AddOrUpdateQuestion(guildUser, [message], true, (_, gemini) => message.ReplyAsync(gemini));
                }
                else
                {
                    _logger.LogDebug("Ignoring reply from {User} to the bot as there's no active conversation", guildUser.Username);
                }

                return Task.CompletedTask;
            }
        }

        if (ShouldIgnoreUser(guildUser))
        {
            _logger.LogDebug("Ignoring message from {Author} as their join date {JoinDate} is older than {IgnoreTime}", guildUser.Username,
                guildUser.JoinedAt ?? default, _config.IgnoreUserAfter);
            return Task.CompletedTask;
        }

        if (IsUsageCapMet(guildUser))
        {
            message.ReplyAsync("You've used this bot a lot recently. Please use Gemini yourself to continue.");
            _logger.LogDebug("{User} has hit their usage cap", guildUser.Username);
            return Task.CompletedTask;
        }

        if (message.Type is MessageType.Reply)
        {
            _logger.LogDebug("{User} is replying to something else, unrelated, dismissing", guildUser.Username);
            return Task.CompletedTask;
        }

        // Handle new conversations
        _messageProcessingService.AddOrUpdateQuestion(guildUser, [message], false, (_, gemini) => message.ReplyAsync(gemini));
        return Task.CompletedTask;
    }

    private bool ShouldIgnoreUser(SocketGuildUser guildUser)
    {
        return !guildUser.JoinedAt.HasValue || TimeProvider.System.GetUtcNow() - guildUser.JoinedAt.Value > _config.IgnoreUserAfter;
    }

    private bool IsUsageCapMet(SocketGuildUser guildUser)
    {
        return guildUser.Roles.All(role => role.Id == guildUser.Guild.EveryoneRole.Id) &&
               _messageProcessingService.IsUsageCapMet(guildUser.Id);
    }

    private Task Log(LogMessage msg)
    {
        var message = msg.ToString(prependTimestamp: false);
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
                _logger.LogCritical("{Message}", message);
                break;
            case LogSeverity.Error:
                _logger.LogError("{Message}", message);
                break;
            case LogSeverity.Warning:
                _logger.LogWarning("{Message}", message);
                break;
            case LogSeverity.Info:
                _logger.LogInformation("{Message}", message);
                break;
            case LogSeverity.Verbose:
                _logger.LogTrace("{Message}", message);
                break;
            case LogSeverity.Debug:
                _logger.LogDebug("{Message}", message);
                break;
        }

        return Task.CompletedTask;
    }
}
