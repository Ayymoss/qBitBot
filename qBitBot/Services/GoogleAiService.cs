﻿using GenerativeAI.Classes;
using GenerativeAI.Models;
using GenerativeAI.Types;
using qBitBot.Models;
using qBitBot.Utilities;

namespace qBitBot.Services;

public class GoogleAiService(Configuration config, ILogger<GoogleAiService> logger)
{
    private readonly Gemini15Pro _model = new(config.GeminiToken)
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
                case PromptContentBase.PromptImage image:
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
                case PromptContentBase.PromptText text:
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

        return await GenerateResponseAsync(parts, cancellationToken);
    }

    public async Task<EnhancedGenerateContentResponse> GenerateResponseAsync(List<Part> parts, CancellationToken cancellationToken)
    {
        logger.LogDebug("Sending parts to Gemini...");
        var response = await _model.GenerateContentAsync(parts, cancellationToken);
        logger.LogDebug("Received response from Gemini...");
        return response;
    }
}
