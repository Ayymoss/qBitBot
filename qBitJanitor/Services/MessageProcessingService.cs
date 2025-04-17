using Humanizer;
using Microsoft.Extensions.Logging;
using qBitJanitor.Enums;

namespace qBitJanitor.Services;

public class MessageProcessingService(GeminiService geminiService, ILogger<MessageProcessingService> logger)
{
    private readonly Dictionary<ulong, List<DateTimeOffset>> _geminiUsage = new();

    public delegate Task GeminiSuccessfulResponseDelegate(bool success, string message);

    public async Task RespondToUserAsync(ulong userId, string userMessage, GeminiSuccessfulResponseDelegate responseDelegate)
    {
        var result = await geminiService.GetGeminiResponseAsync(userMessage);
        if (result is null)
        {
            await responseDelegate(false, "Error: No response from Gemini.");
            return;
        }

        var metaMessage = $"**Classification:** {result.MessageType.Humanize().Titleize()}\n" +
                          $"**Note:** {result.AnalysisNotes ?? "No analysis note."}\n" +
                          $"-# This is a system message.";

        switch (result.MessageType)
        {
            case MessageClassification.IncompleteQuestion when result.FeedbackForUser is not null:
            {
                await responseDelegate(true, result.FeedbackForUser);
                break;
            }
            case MessageClassification.CompleteQuestion:
            {
                await responseDelegate(false, metaMessage);
                logger.LogInformation("User {UserId} asked a complete question", userId);
                break;
            }
            case MessageClassification.NonQuestion:
            {
                await responseDelegate(false, metaMessage);
                logger.LogInformation("User {UserId} didn't ask a question {Message}", userId, userMessage);
                break;
            }
            default:
                throw new ArgumentOutOfRangeException();
        }

        if (_geminiUsage.TryGetValue(userId, out var usage))
        {
            usage.Add(DateTimeOffset.UtcNow);
        }
        else
        {
            // This should not hit as we create the dictionary entry in HasMetUsageCapacity
            _geminiUsage.Add(userId, [DateTimeOffset.UtcNow]);
        }
    }

    public bool HasMetUsageCapacity(ulong userId)
    {
        if (!_geminiUsage.TryGetValue(userId, out var usage))
        {
            _geminiUsage.Add(userId, []);
            return false;
        }

        var now = TimeProvider.System.GetUtcNow();
        usage.RemoveAll(x => x < now.AddDays(-1));

        return usage.Count >= 10;
    }
}
