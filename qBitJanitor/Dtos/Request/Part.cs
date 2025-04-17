using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class Part
{
    [JsonPropertyName("text")] public string? Text { get; set; }
}
