using qBitJanitor.Dtos.Request;

namespace qBitJanitor.Utilities;

public static class GeminiHelper
{
    public static GeminiRequest CreateGeminiRequest(string userInput, string systemInstructionText)
    {
        return new GeminiRequest
        {
            SafetySettings =
            [
                new SafetySetting { Category = "HARM_CATEGORY_HARASSMENT", Threshold = "BLOCK_ONLY_HIGH" },
                new SafetySetting { Category = "HARM_CATEGORY_HATE_SPEECH", Threshold = "BLOCK_ONLY_HIGH" },
                new SafetySetting { Category = "HARM_CATEGORY_SEXUALLY_EXPLICIT", Threshold = "BLOCK_ONLY_HIGH" },
                new SafetySetting { Category = "HARM_CATEGORY_DANGEROUS_CONTENT", Threshold = "BLOCK_NONE" }
            ],
            Contents =
            [
                new Content
                {
                    Role = "user",
                    Parts = [new Part { Text = userInput }]
                }
            ],
            SystemInstruction = new SystemInstruction
            {
                Parts = [new Part { Text = systemInstructionText }]
            },
            GenerationConfig = new GenerationConfig
            {
                ResponseMimeType = "application/json",
                ResponseSchema = new ResponseSchema
                {
                    Type = "object",
                    Properties = new Dictionary<string, PropertyDefinition>
                    {
                        ["message_type"] = new()
                        {
                            Type = "string",
                            Description = "Classification of the user message.",
                            Enum = ["COMPLETE_QUESTION", "INCOMPLETE_QUESTION", "NON_QUESTION"]
                        },
                        ["analysis_notes"] = new()
                        {
                            Type = "string",
                            Description = "Optional internal notes explaining the reasoning for the classification, " +
                                          "especially for borderline cases.",
                        },
                        ["feedback_for_user"] = new()
                        {
                            Type = "string",
                            Description =
                                "Constructive feedback for the user ONLY if the message_type is INCOMPLETE_QUESTION. " +
                                "Explain why it's incomplete and suggest improvements (e.g., 'Please specify the software version,' " +
                                "'Could you describe the exact error?'). Should be null or omitted otherwise.",
                            Nullable = true
                        }
                    },
                    Required = ["message_type"]
                }
            }
        };
    }
}
