using Discord;
using Discord.WebSocket;
using GenerativeAI.Classes;
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

    public DiscordBotService(DiscordSocketClient client, ILogger<DiscordBotService> logger, Configuration config,
        MessageProcessingService messageProcessingService, HttpClient httpClient)
    {
        _client = client;
        _logger = logger;
        _config = config;
        _messageProcessingService = messageProcessingService;
        _httpClient = httpClient;

        _client.Log += Log;
        _client.MessageReceived += MessageReceivedAsync;
    }

    public async Task StartBotAsync()
    {
        await _client.LoginAsync(TokenType.Bot, _config.BotToken);
        await _client.SetStatusAsync(UserStatus.Online);
        await _client.StartAsync();
    }

    private async Task MessageReceivedAsync(SocketMessage socketMessage)
    {
        if (socketMessage.Author.IsBot || socketMessage.Author.IsWebhook) return;
        if (socketMessage is not SocketUserMessage message) return;

        // Someone else already responded to their question.
        var answered = _messageProcessingService.IsAnsweredQuestion(message.Author, message.ReferencedMessage.Author);
        if (message.Type is MessageType.Reply && answered) return;


        List<PromptContentBase> prompts = [];

        if (message.Attachments.Count is not 0)
        {
            foreach (var attachment in message.Attachments)
            {
                if (!attachment.ContentType.StartsWith("image/")) continue;

                var response = await _httpClient.GetAsync(attachment.ProxyUrl);
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

        _messageProcessingService.AddQuestion(socketMessage.Author, message, prompts);
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
