using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class ResponseSchema
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("properties")] public Dictionary<string, PropertyDefinition>? Properties { get; set; }
    [JsonPropertyName("required")] public List<string>? Required { get; set; }
}
