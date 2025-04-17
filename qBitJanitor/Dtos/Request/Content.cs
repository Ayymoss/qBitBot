using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class Content
{
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("parts")] public List<Part>? Parts { get; set; }
}
