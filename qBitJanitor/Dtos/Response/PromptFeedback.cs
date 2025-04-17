using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Response;

public class PromptFeedback
{
    [JsonPropertyName("safetyRatings")] public List<SafetyRating>? SafetyRatings { get; set; }
}
