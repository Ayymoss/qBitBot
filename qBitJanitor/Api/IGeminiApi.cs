using qBitJanitor.Dtos.Request;
using Refit;

namespace qBitJanitor.Api;

public interface IGeminiApi
{
    [Post("/models/{modelId}:{generateContentApi}")]
    Task<HttpResponseMessage> GenerateContentStreamAsync(string modelId, string generateContentApi, [Query] string key,
        [Body] GeminiRequest request);
}
