using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class GeminiRequest
{
    [JsonPropertyName("safetySettings")] public List<SafetySetting>? SafetySettings { get; set; }
    [JsonPropertyName("contents")] public List<Content>? Contents { get; set; }

    [JsonPropertyName("systemInstruction")]
    public SystemInstruction? SystemInstruction { get; set; }

    [JsonPropertyName("generationConfig")] public GenerationConfig? GenerationConfig { get; set; }
}
