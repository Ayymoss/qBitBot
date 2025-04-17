using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class SafetySetting
{
    [JsonPropertyName("category")] public string? Category { get; set; }
    [JsonPropertyName("threshold")] public string? Threshold { get; set; }
}
