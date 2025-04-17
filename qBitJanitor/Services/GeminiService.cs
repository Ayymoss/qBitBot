using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using qBitJanitor.Api;
using qBitJanitor.Dtos.Response;
using qBitJanitor.Utilities;

namespace qBitJanitor.Services;

public class GeminiService(
    IGeminiApi geminiApi,
    IOptionsMonitor<Configuration> optionsMonitor,
    JsonSerializerOptions jsonOptions,
    ILogger<GeminiService> logger)
{
    private readonly Configuration _configuration = optionsMonitor.CurrentValue;
    private readonly JsonSerializerOptions _jsonIndented = new() { WriteIndented = true };

    private const string ModelId = "gemini-1.5-flash";
    private const string GenerateContentApi = "streamGenerateContent";

    // @formatter:off
    private const string SystemPrompt = "You are an AI assistant tasked with evaluating user messages based on their intent and completeness as a question specific to qBittorrent client. Your goal is to classify the message and provide feedback *only* when the question is incomplete.\\n\\nClassification Categories (`message_type`):\\n\\n1.  **COMPLETE_QUESTION**: Meets all criteria for a complete and actionable question.\\n    *   Provides Sufficient Context: Mentions relevant software, tools, environment, versions, or situation.\\n    *   Describes a Specific Problem or Goal: Clearly explains the issue, what was tried, or the specific information needed.\\n    *   Implies or Asks for Specific Help: Leads to a point where someone could provide a direct answer or solution.\\n    *   Is NOT a Meta-Question or Vague Plea: Avoids \\\"Can I ask?\\\", \\\"Anyone here?\\\", \\\"Help with X?\\\".\\n\\n2.  **INCOMPLETE_QUESTION**: Appears to be an attempt to ask a question or seek help but fails one or more criteria for a complete question (e.g., lacks context, vague, meta-question).\\n\\n3.  **NON_QUESTION**: The message does not appear to be asking a question or seeking help. It might be a statement, greeting, comment, or unrelated chatter.\\n\\n**Output Field Rules:**\\n\\n*   **`message_type` (Required):** Assign one of the three categories above based on your analysis.\\n*   **`feedback_for_user` (Conditional):**\\n    *   **ONLY populate this field if `message_type` is `INCOMPLETE_QUESTION`.**\\n    *   The feedback must be constructive, polite, and guide the user on how to improve their question (e.g., \\\"To help you better, could you please specify which software version you are using?\\\", \\\"Could you describe the steps you took before seeing the error?\\\").\\n    *   **If `message_type` is `COMPLETE_QUESTION` or `NON_QUESTION`, this field MUST be `null` or omitted.** Do NOT generate feedback for these cases.\\n*   **`analysis_notes` (Optional):** You can use this field to record your internal reasoning for the classification, especially if it's a complex or borderline case. This is not shown to the user.\\n\\nAnalyze the user message below and generate the structured output according to the schema and rules.\\n\\nInput Message:";
    // @formatter:on

    public async Task<GeminiResponse?> GetGeminiResponseAsync(string userMessage)
    {
        var request = GeminiHelper.CreateGeminiRequest(userMessage, SystemPrompt);

        HttpResponseMessage? response = null;
        try
        {
            response = await geminiApi.GenerateContentStreamAsync(ModelId, GenerateContentApi, _configuration.GeminiToken, request);

            if (response.IsSuccessStatusCode)
            {
                var fullResponseContent = await response.Content.ReadAsStringAsync();
                var chunks = JsonSerializer.Deserialize<List<GenerateContentResponseChunk>>(fullResponseContent, jsonOptions);

                if (chunks != null && chunks.Count != 0)
                {
                    var rawResult = chunks
                        .Where(chunk => chunk.Candidates != null)
                        .SelectMany(chunk => chunk.Candidates!)
                        .Where(candidate => candidate.Content?.Parts != null)
                        .SelectMany(candidate => candidate.Content!.Parts!)
                        .Select(part => part.Text);

                    var extractedJsonText = string.Join("", rawResult);

                    try
                    {
                        var finalResponse = JsonSerializer.Deserialize<GeminiResponse>(extractedJsonText, jsonOptions);

                        if (finalResponse is not null)
                        {
                            if (finalResponse.FeedbackForUser is not null)
                            {
                                finalResponse.FeedbackForUser = finalResponse.FeedbackForUser.Replace("  ", " ");
                            }

                            if (finalResponse.AnalysisNotes is not null)
                            {
                                finalResponse.AnalysisNotes = finalResponse.AnalysisNotes.Replace("  ", " ");
                            }

                            return finalResponse;
                        }

                        logger.LogError("Deserialization failed: finalResponse is null");
                    }
                    catch (JsonException jsonEx)
                    {
                        logger.LogError("Failed to deserialize the extracted JSON text into GeminiResponse: {JsonExMessage}",
                            jsonEx.Message);
                        logger.LogError("Extracted Text was: {ExtractedJsonText}", extractedJsonText);
                    }
                }
                else
                {
                    logger.LogWarning("Warning: Stream received and parsed, but no valid chunks with candidates were found");
                    logger.LogWarning("Check the 'Raw Stream Content' above");
                }
            }
            else
            {
                logger.LogError("API Error: {ResponseStatusCode} {ResponseReasonPhrase}", (int)response.StatusCode, response.ReasonPhrase);
                var errorContent = await response.Content.ReadAsStringAsync();
                try
                {
                    using var doc = JsonDocument.Parse(errorContent);
                    logger.LogError("Error Details: {SerialisedDoc}", JsonSerializer.Serialize(doc, _jsonIndented));
                }
                catch
                {
                    logger.LogError("Error Details: {Content}", errorContent);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError("Unexpected Error: {Exception}", ex);
        }
        finally
        {
            response?.Dispose();
        }

        return null;
    }
}
