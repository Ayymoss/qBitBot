using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using GenerativeAI.Classes;
using qBitBot.Commands;
using qBitBot.Models;
using qBitBot.Utilities;

namespace qBitBot.Services;

public class DiscordBotService
{
    private readonly DiscordSocketClient _client;
    private readonly ILogger<DiscordBotService> _logger;
    private readonly Configuration _config;
    private readonly MessageProcessingService _messageProcessingService;
    private readonly HttpClient _httpClient;
    private readonly IServiceProvider _serviceProvider;

    public DiscordBotService(DiscordSocketClient client, ILogger<DiscordBotService> logger, Configuration config,
        MessageProcessingService messageProcessingService, HttpClient httpClient, IServiceProvider serviceProvider)
    {
        _client = client;
        _logger = logger;
        _config = config;
        _httpClient = httpClient;
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

        // Someone else already responded to their question.
        if (message.Type is MessageType.Reply && _messageProcessingService
                .IsAnsweredQuestion(guildUser, message.ReferencedMessage.Author)) return Task.CompletedTask;

        // Ignore messages where the user has established themselves in the Discord.
        if (!guildUser.JoinedAt.HasValue || guildUser.JoinedAt.Value.Add(_config.IgnoreQuestionsAfter) < TimeProvider.System.GetUtcNow())
        {
            _logger.LogDebug("Ignoring message from {Author} as their join date {JoinDate} is older than {IgnoreTime}", guildUser.Username,
                guildUser.JoinedAt ?? default, _config.IgnoreQuestionsAfter);
            return Task.CompletedTask;
        }

        _messageProcessingService.AddOrUpdateQuestion(guildUser, message, false);
        return Task.CompletedTask;
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
