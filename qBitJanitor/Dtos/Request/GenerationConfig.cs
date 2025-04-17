using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class GenerationConfig
{
    [JsonPropertyName("responseMimeType")] public string? ResponseMimeType { get; set; }
    [JsonPropertyName("responseSchema")] public ResponseSchema? ResponseSchema { get; set; }
}
