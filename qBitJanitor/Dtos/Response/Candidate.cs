﻿using System.Text.Json.Serialization;
using qBitJanitor.Dtos.Request;

namespace qBitJanitor.Dtos.Response;

public class Candidate
{
    [JsonPropertyName("content")] public Content? Content { get; set; }
    [JsonPropertyName("finishReason")] public string? FinishReason { get; set; }
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("safetyRatings")] public List<SafetyRating>? SafetyRatings { get; set; }
}
