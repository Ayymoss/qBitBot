using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Response;

public class GenerateContentResponseChunk
{
    [JsonPropertyName("candidates")] public List<Candidate>? Candidates { get; set; }
    [JsonPropertyName("promptFeedback")] public PromptFeedback? PromptFeedback { get; set; }
}
