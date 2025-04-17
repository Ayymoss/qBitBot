using System.Text.Json.Serialization;
using qBitJanitor.Enums;
using qBitJanitor.Utilities;

namespace qBitJanitor.Dtos.Response;

public class GeminiResponse
{
    [JsonPropertyName("message_type"), JsonConverter(typeof(MessageClassificationEnumConverter))]
    public MessageClassification MessageType { get; set; }

    [JsonPropertyName("analysis_notes")] public string? AnalysisNotes { get; set; }

    [JsonPropertyName("feedback_for_user")]
    public string? FeedbackForUser { get; set; }
}
