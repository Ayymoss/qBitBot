using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class SystemInstruction
{
    [JsonPropertyName("parts")] public List<Part>? Parts { get; set; }
}
