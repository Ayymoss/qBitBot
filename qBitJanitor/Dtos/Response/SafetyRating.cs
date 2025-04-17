using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Response;

public class SafetyRating
{
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("probability")] public string? Probability { get; set; }
}
