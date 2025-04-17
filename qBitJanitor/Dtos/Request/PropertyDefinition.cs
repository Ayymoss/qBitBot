using System.Text.Json.Serialization;

namespace qBitJanitor.Dtos.Request;

public class PropertyDefinition
{
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("enum"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Enum { get; set; }

    [JsonPropertyName("nullable"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Nullable { get; set; }
}
