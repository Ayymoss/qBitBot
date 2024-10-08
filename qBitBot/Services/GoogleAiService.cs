using GenerativeAI.Classes;
using GenerativeAI.Models;
using GenerativeAI.Types;
using qBitBot.Models;
using qBitBot.Utilities;

namespace qBitBot.Services;

public class GoogleAiService(Configuration config, ILogger<GoogleAiService> logger)
{
    private readonly Gemini15Flash _model = new(config.GeminiToken)
        {
            SafetySettings =
            [
                new SafetySetting
                {
                    Category = HarmCategory.HARM_CATEGORY_HARASSMENT,
                    Threshold = HarmBlockThreshold.BLOCK_ONLY_HIGH
                },
                new SafetySetting
                {
                    Category = HarmCategory.HARM_CATEGORY_HATE_SPEECH,
                    Threshold = HarmBlockThreshold.BLOCK_ONLY_HIGH
                },
                new SafetySetting
                {
                    Category = HarmCategory.HARM_CATEGORY_DANGEROUS_CONTENT,
                    Threshold = HarmBlockThreshold.BLOCK_NONE
                },
                new SafetySetting
                {
                    Category = HarmCategory.HARM_CATEGORY_SEXUALLY_EXPLICIT,
                    Threshold = HarmBlockThreshold.BLOCK_ONLY_HIGH
                }
            ]
        };

    public async Task<EnhancedGenerateContentResponse> GenerateResponseAsync(IEnumerable<PromptContentBase> prompts,
        CancellationToken cancellationToken)
    {
        List<Part> parts = [];

        logger.LogDebug("Sanitizing prompts for Gemini's API...");
        foreach (var prompt in prompts)
        {
            switch (prompt)
            {
                case PromptImage image:
                {
                    var imagePart = new Part
                    {
                        InlineData = new GenerativeContentBlob
                        {
                            MimeType = MimeTypeHelper.GetMimeType(image.Image.FileName),
                            Data = Convert.ToBase64String(image.Image.FileContent)
                        }
                    };
                    parts.Add(imagePart);
                    break;
                }
                case PromptText text:
                {
                    var textPart = new Part
                    {
                        Text = text.Text,
                    };
                    parts.Add(textPart);
                    break;
                }
            }
        }

        logger.LogDebug("Sending parts to Gemini...");
        return await GenerateResponseAsync(parts, cancellationToken);
    }

    public async Task<EnhancedGenerateContentResponse> GenerateResponseAsync(List<Part> parts, CancellationToken cancellationToken)
    {
        return await _model.GenerateContentAsync(parts, cancellationToken);
    }
}
