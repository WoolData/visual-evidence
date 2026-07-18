// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Net.Http.Headers;
using System.Globalization;
using System.Text;

namespace WoolData.VisualEvidence;

public sealed class OpenAiCompatibleImageReviewProvider : IImageReviewProvider, IDisposable
{
    private readonly OpenAiCompatibleImageReviewOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;

    public OpenAiCompatibleImageReviewProvider(OpenAiCompatibleImageReviewOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new ArgumentException("OpenAI-compatible model is required.", nameof(options));
        }
        if (string.IsNullOrWhiteSpace(options.ProviderName) ||
            options.ProviderName.Length > 100 ||
            ContainsUnsafeProviderNameCharacter(options.ProviderName))
        {
            throw new ArgumentException("OpenAI-compatible provider name must contain 1 to 100 printable characters.", nameof(options));
        }

        _options = options;
        Uri baseUri = AiReviewProviderProtocol.ValidateBaseUri(httpClient?.BaseAddress ?? options.BaseUri);
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress = baseUri;
    }

    private static bool ContainsUnsafeProviderNameCharacter(string value)
    {
        for (int index = 0; index < value.Length;)
        {
            if (!Rune.TryGetRuneAt(value, index, out Rune rune))
            {
                return true;
            }

            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            int scalar = rune.Value;
            bool isVariationSelector =
                (scalar >= 0x180B && scalar <= 0x180D) ||
                scalar == 0x180F ||
                (scalar >= 0xFE00 && scalar <= 0xFE0F) ||
                (scalar >= 0xE0100 && scalar <= 0xE01EF);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format ||
                isVariationSelector)
            {
                return true;
            }

            index += rune.Utf16SequenceLength;
        }

        return false;
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
            Provider = _options.ProviderName,
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
            byte[] body = AiReviewProviderProtocol.BuildOpenAiRequest(
                _options.Model,
                request.Prompt,
                pair,
                correction: attempt > 0);
            using HttpRequestMessage message = AiReviewProviderProtocol.CreateJsonRequest(HttpMethod.Post, "chat/completions", body);
            if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);
            }
            byte[] response = await AiReviewProviderProtocol.SendAsync(
                _httpClient,
                message,
                _options.ApiKey,
                cancellationToken).ConfigureAwait(false);
            try
            {
                return AiReviewProviderProtocol.ParseEntry(
                    AiReviewProviderProtocol.ExtractOpenAiContent(response),
                    pair);
            }
            catch (EvidenceValidationException ex)
            {
                validationFailure = ex;
            }
        }
        throw new AiReviewProviderException("OpenAI-compatible provider returned invalid structured review output twice.", validationFailure);
    }

    private static void ValidateRequest(AiReviewRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Task != "compare" || request.Captures.Count == 0)
        {
            throw new ArgumentException("OpenAI-compatible provider currently supports non-empty compare requests only.");
        }
        if (!string.Equals(AiReviewPrompt.CalculateSha256(request.Prompt), request.PromptSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new EvidenceValidationException("AI review request prompt hash does not match the effective prompt.");
        }
    }
}
