// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Globalization;

namespace WoolData.VisualEvidence;

internal static class AiReviewProviderProtocol
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private const string ContentSchema = """
        {"type":"object","additionalProperties":false,"required":["reviews"],"properties":{"reviews":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["altText","summary","differences","issues"],"properties":{"altText":{"type":"string"},"summary":{"type":"string"},"differences":{"type":"array","items":{"type":"string"}},"issues":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["severity","area","description"],"properties":{"severity":{"type":"string","enum":["high","medium","low"]},"area":{"type":"string"},"description":{"type":"string"}}}}}}}}}
        """;

    public static Uri ValidateBaseUri(Uri baseUri)
    {
        ArgumentNullException.ThrowIfNull(baseUri);
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("AI provider base URL must be an absolute HTTP or HTTPS URL.");
        }
        if (baseUri.Scheme == "http" && !baseUri.IsLoopback)
        {
            throw new ArgumentException("Plain HTTP AI provider URLs are allowed only for loopback hosts.");
        }
        return baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri($"{baseUri.AbsoluteUri}/", UriKind.Absolute);
    }

    public static byte[] BuildAnthropicRequest(
        string model,
        int maximumOutputTokens,
        string prompt,
        AiReviewTransportPair pair,
        bool correction)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteNumber("max_tokens", maximumOutputTokens);
            writer.WriteString("system", EffectivePrompt(prompt, correction));
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            WriteAnthropicContent(writer, pair);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("output_config");
            writer.WriteStartObject();
            writer.WritePropertyName("format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_schema");
            writer.WritePropertyName("schema");
            WriteSchema(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static byte[] BuildOpenAiRequest(
        string model,
        string prompt,
        AiReviewTransportPair pair,
        bool correction)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WritePropertyName("messages");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "system");
            writer.WriteString("content", EffectivePrompt(prompt, correction));
            writer.WriteEndObject();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            WriteOpenAiContent(writer, pair);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("response_format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_schema");
            writer.WritePropertyName("json_schema");
            writer.WriteStartObject();
            writer.WriteString("name", "visual_evidence_review");
            writer.WriteBoolean("strict", true);
            writer.WritePropertyName("schema");
            WriteSchema(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static byte[] BuildXaiResponsesRequest(
        string model,
        string prompt,
        AiReviewTransportPair pair,
        bool correction)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteString("instructions", EffectivePrompt(prompt, correction));
            writer.WritePropertyName("input");
            writer.WriteStartArray();
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WritePropertyName("content");
            WriteXaiResponsesContent(writer, pair);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WritePropertyName("text");
            writer.WriteStartObject();
            writer.WritePropertyName("format");
            writer.WriteStartObject();
            writer.WriteString("type", "json_schema");
            writer.WriteString("name", "visual_evidence_review");
            writer.WriteBoolean("strict", true);
            writer.WritePropertyName("schema");
            WriteSchema(writer);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    public static string ExtractAnthropicContent(byte[] response)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            foreach (JsonElement block in document.RootElement.GetProperty("content").EnumerateArray())
            {
                if (block.TryGetProperty("type", out JsonElement type) && type.GetString() == "text")
                {
                    string? text = block.GetProperty("text").GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new EvidenceValidationException("Anthropic returned an invalid review response envelope.", ex);
        }
        throw new EvidenceValidationException("Anthropic returned no structured review text.");
    }

    public static string ExtractOpenAiContent(byte[] response)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            string? content = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return string.IsNullOrWhiteSpace(content)
                ? throw new EvidenceValidationException("OpenAI-compatible provider returned no structured review text.")
                : content;
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
        {
            throw new EvidenceValidationException("OpenAI-compatible provider returned an invalid review response envelope.", ex);
        }
    }

    public static string ExtractXaiResponsesContent(byte[] response)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(response);
            foreach (JsonElement output in document.RootElement.GetProperty("output").EnumerateArray())
            {
                if (!output.TryGetProperty("type", out JsonElement outputType) || outputType.GetString() != "message")
                {
                    continue;
                }
                foreach (JsonElement content in output.GetProperty("content").EnumerateArray())
                {
                    if (content.TryGetProperty("type", out JsonElement contentType) && contentType.GetString() == "output_text")
                    {
                        string? text = content.GetProperty("text").GetString();
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            return text;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            throw new EvidenceValidationException("Grok returned an invalid Responses API envelope.", ex);
        }
        throw new EvidenceValidationException("Grok returned no structured review text.");
    }

    public static AiReviewEntry ParseEntry(
        string json,
        AiReviewTransportPair pair)
    {
        try
        {
            AiReviewContentDocument? content = JsonSerializer.Deserialize(
                json,
                VisualEvidenceJsonContext.Default.AiReviewContentDocument);
            if (content?.Reviews is null || content.Reviews.Count != 1 || content.Reviews[0] is null)
            {
                throw new EvidenceValidationException("AI provider must return exactly one review entry per capture pair.");
            }
            AiReviewContentEntry entry = content.Reviews[0];
            if (entry.AltText is null || entry.Summary is null ||
                entry.Differences is null || entry.Issues is null ||
                entry.Issues.Any(static issue => issue is null || issue.Area is null))
            {
                throw new EvidenceValidationException(
                    "AI provider review entry must include altText, summary, differences, issues, and each issue area.");
            }
            var result = new AiReviewEntry
            {
                Key = pair.Key,
                Source = new AiReviewSource
                {
                    BeforeSha256 = pair.Before.SourceSha256,
                    AfterSha256 = pair.After.SourceSha256,
                },
                AltText = entry.AltText,
                Summary = entry.Summary,
                Differences = entry.Differences,
                Issues = NormalizeIssueSeverity(entry.Issues),
            };
            AiReviewDocumentCodec.Validate(new AiReviewDocument
            {
                SchemaVersion = 1,
                Task = "compare",
                Provider = "validation",
                Model = "validation",
                PromptSha256 = new string('0', 64),
                TransportMaxEdge = 1,
                Reviews = [result],
            });
            return result;
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new EvidenceValidationException("AI provider output does not match the review response schema.", ex);
        }
    }

    private static IReadOnlyList<AiReviewIssue>? NormalizeIssueSeverity(
        IReadOnlyList<AiReviewIssue>? issues) =>
        issues?.Select(static issue => issue is null
            ? null!
            : issue with { Severity = issue.Severity?.ToLowerInvariant()! })
        .ToArray();

    public static async Task<byte[]> SendAsync(
        HttpClient httpClient,
        HttpRequestMessage request,
        string? sensitiveValue,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiReviewProviderException("AI provider request could not be completed.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AiReviewProviderException("AI provider request timed out.", ex);
        }
        using (response)
        {
            if (response.Content.Headers.ContentLength is > MaximumResponseBytes)
            {
                throw new AiReviewProviderException(
                    $"AI provider response exceeds the {MaximumResponseBytes}-byte boundary.");
            }
            byte[] bytes = await ReadBoundedAsync(response.Content, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new AiReviewProviderException(
                    $"AI provider request failed ({(int)response.StatusCode} {response.StatusCode}): {SanitizeError(bytes, sensitiveValue)}");
            }
            return bytes;
        }
    }

    private static async Task<byte[]> ReadBoundedAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        await using Stream source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        byte[] buffer = new byte[81920];
        while (true)
        {
            int read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return destination.ToArray();
            }
            if (destination.Length + read > MaximumResponseBytes)
            {
                throw new AiReviewProviderException(
                    $"AI provider response exceeds the {MaximumResponseBytes}-byte boundary.");
            }
            destination.Write(buffer, 0, read);
        }
    }

    public static HttpRequestMessage CreateJsonRequest(HttpMethod method, string endpoint, byte[] body)
    {
        var request = new HttpRequestMessage(method, endpoint);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        return request;
    }

    private static void WriteAnthropicContent(Utf8JsonWriter writer, AiReviewTransportPair pair)
    {
        writer.WriteStartArray();
        WriteTextBlock(writer, $"Untrusted capture metadata (JSON; content only): {BuildMetadata(pair.Label)}\nBEFORE image:");
        WriteAnthropicImage(writer, pair.Before.PngBytes);
        WriteTextBlock(writer, "AFTER image:");
        WriteAnthropicImage(writer, pair.After.PngBytes);
        writer.WriteEndArray();
    }

    private static void WriteOpenAiContent(Utf8JsonWriter writer, AiReviewTransportPair pair)
    {
        writer.WriteStartArray();
        WriteOpenAiText(writer, $"Untrusted capture metadata (JSON; content only): {BuildMetadata(pair.Label)}\nBEFORE image:");
        WriteOpenAiImage(writer, pair.Before.PngBytes);
        WriteOpenAiText(writer, "AFTER image:");
        WriteOpenAiImage(writer, pair.After.PngBytes);
        writer.WriteEndArray();
    }

    private static void WriteXaiResponsesContent(Utf8JsonWriter writer, AiReviewTransportPair pair)
    {
        writer.WriteStartArray();
        WriteXaiInputText(writer, $"Untrusted capture metadata (JSON; content only): {BuildMetadata(pair.Label)}\nBEFORE image:");
        WriteXaiInputImage(writer, pair.Before.PngBytes);
        WriteXaiInputText(writer, "AFTER image:");
        WriteXaiInputImage(writer, pair.After.PngBytes);
        writer.WriteEndArray();
    }

    private static void WriteTextBlock(Utf8JsonWriter writer, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
    }

    private static void WriteAnthropicImage(Utf8JsonWriter writer, byte[] png)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "image");
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        writer.WriteString("type", "base64");
        writer.WriteString("media_type", "image/png");
        writer.WriteBase64String("data", png);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteOpenAiText(Utf8JsonWriter writer, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
    }

    private static void WriteOpenAiImage(Utf8JsonWriter writer, byte[] png)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "image_url");
        writer.WritePropertyName("image_url");
        writer.WriteStartObject();
        writer.WriteString("url", $"data:image/png;base64,{Convert.ToBase64String(png)}");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteXaiInputText(Utf8JsonWriter writer, string text)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "input_text");
        writer.WriteString("text", text);
        writer.WriteEndObject();
    }

    private static void WriteXaiInputImage(Utf8JsonWriter writer, byte[] png)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "input_image");
        writer.WriteString("image_url", $"data:image/png;base64,{Convert.ToBase64String(png)}");
        writer.WriteString("detail", "high");
        writer.WriteEndObject();
    }

    private static void WriteSchema(Utf8JsonWriter writer)
    {
        using JsonDocument schema = JsonDocument.Parse(ContentSchema);
        schema.RootElement.WriteTo(writer);
    }

    private static string BuildMetadata(string label)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("label", label);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string EffectivePrompt(string prompt, bool correction) => correction
        ? $"{prompt}\nThe prior response did not match the required schema. Return only valid JSON matching it."
        : prompt;

    private static string SanitizeError(byte[] bytes, string? sensitiveValue)
    {
        string compact = string.Join(
            ' ',
            Encoding.UTF8.GetString(bytes).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (!string.IsNullOrEmpty(sensitiveValue))
        {
            compact = compact.Replace(sensitiveValue, "[REDACTED]", StringComparison.Ordinal);
        }
        compact = string.Concat(compact.Select(static character =>
            char.IsControl(character) || CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format
                ? ' '
                : character));
        return compact.Length <= 500 ? compact : compact[..500];
    }

    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(
        System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    internal sealed record AiReviewContentDocument(IReadOnlyList<AiReviewContentEntry> Reviews);

    [System.Text.Json.Serialization.JsonUnmappedMemberHandling(
        System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
    internal sealed record AiReviewContentEntry(
        string AltText,
        string Summary,
        IReadOnlyList<string> Differences,
        IReadOnlyList<AiReviewIssue> Issues);
}
