using System.Text.Json;
using System.Text.Json.Serialization;
using qBitJanitor.Enums;

namespace qBitJanitor.Utilities;

public class MessageClassificationEnumConverter : JsonConverter<MessageClassification>
{
    public override MessageClassification Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var webEvent = reader.GetString();
        return webEvent switch
        {
            "COMPLETE_QUESTION" => MessageClassification.CompleteQuestion,
            "INCOMPLETE_QUESTION" => MessageClassification.IncompleteQuestion,
            "NON_QUESTION" => MessageClassification.NonQuestion,
            _ => throw new JsonException($"Failed to convert string to enum: {webEvent}")
        };
    }

    public override void Write(Utf8JsonWriter writer, MessageClassification value, JsonSerializerOptions options)
    {
        throw new NotImplementedException();
    }
}
