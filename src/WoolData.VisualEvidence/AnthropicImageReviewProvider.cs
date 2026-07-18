// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public sealed class AnthropicImageReviewProvider : IImageReviewProvider, IDisposable
{
    private readonly AnthropicImageReviewOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public AnthropicImageReviewProvider(AnthropicImageReviewOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new ArgumentException("Anthropic API key is required.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ArgumentException("Anthropic model is required.", nameof(options));
        }
        if (options.MaximumOutputTokens is < 1 or > 64_000)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Maximum output tokens must be between 1 and 64000.");
        }

        _options = options;
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        Uri baseUri = AiReviewProviderProtocol.ValidateBaseUri(_httpClient.BaseAddress ?? options.BaseUri);
        _httpClient.BaseAddress = baseUri;
    }

    public async Task<AiReviewDocument> ReviewAsync(
        AiReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        var entries = new List<AiReviewEntry>(request.Captures.Count);
        foreach (AiReviewTransportPair pair in request.Captures)
        {
            entries.Add(await ReviewPairAsync(request, pair, cancellationToken).ConfigureAwait(false));
        }

        var document = new AiReviewDocument
        {
            SchemaVersion = 1,
            Task = request.Task,
            Provider = "anthropic",
            Model = _options.Model,
            PromptSha256 = request.PromptSha256,
            TransportMaxEdge = request.TransportMaxEdge,
            Reviews = entries,
        };
        AiReviewDocumentCodec.Validate(document);
        return document;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<AiReviewEntry> ReviewPairAsync(
        AiReviewRequest request,
        AiReviewTransportPair pair,
        CancellationToken cancellationToken)
    {
        EvidenceValidationException? validationFailure = null;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            byte[] body = AiReviewProviderProtocol.BuildAnthropicRequest(
                _options.Model,
                _options.MaximumOutputTokens,
                request.Prompt,
                pair,
                correction: attempt > 0);
            using HttpRequestMessage message = AiReviewProviderProtocol.CreateJsonRequest(HttpMethod.Post, "v1/messages", body);
            message.Headers.Add("x-api-key", _options.ApiKey);
            message.Headers.Add("anthropic-version", "2023-06-01");
            byte[] response = await AiReviewProviderProtocol.SendAsync(_httpClient, message, cancellationToken).ConfigureAwait(false);
            try
            {
                return AiReviewProviderProtocol.ParseEntry(
                    AiReviewProviderProtocol.ExtractAnthropicContent(response),
                    pair);
            }
            catch (EvidenceValidationException ex)
            {
                validationFailure = ex;
            }
        }
        throw new AiReviewProviderException("Anthropic returned invalid structured review output twice.", validationFailure);
    }

    private static void ValidateRequest(AiReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Task != "compare" || request.Captures.Count == 0)
        {
            throw new ArgumentException("Anthropic provider currently supports non-empty compare requests only.");
        }
        if (!string.Equals(AiReviewPrompt.CalculateSha256(request.Prompt), request.PromptSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new EvidenceValidationException("AI review request prompt hash does not match the effective prompt.");
        }
    }
}
