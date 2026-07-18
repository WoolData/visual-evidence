// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Globalization;

namespace WoolData.VisualEvidence;

public static class AiReviewDocumentCodec
{
    public const int MaximumDocumentBytes = 256 * 1024;

    private const int MaximumReviewCount = 50;
    private const int MaximumDifferenceCount = 50;
    private const int MaximumIssueCount = 50;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly VisualEvidenceJsonContext SerializerContext = new(SerializerOptions);

    public static async Task<AiReviewDocument> ReadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumDocumentBytes)
            {
                throw new EvidenceValidationException(
                    $"AI review must be present and no larger than {MaximumDocumentBytes} bytes: {path}");
            }

            byte[] bytes = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
            return Read(bytes);
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EvidenceValidationException($"Could not read AI review '{path}'.", ex);
        }
    }

    public static AiReviewDocument Read(ReadOnlySpan<byte> utf8Json)
    {
        if (utf8Json.Length is <= 0 or > MaximumDocumentBytes)
        {
            throw new EvidenceValidationException(
                $"AI review must contain between 1 and {MaximumDocumentBytes} bytes.");
        }

        try
        {
            RejectDuplicateProperties(utf8Json);
            AiReviewDocument? document = JsonSerializer.Deserialize(
                utf8Json,
                SerializerContext.AiReviewDocument);
            if (document is null)
            {
                throw new EvidenceValidationException("AI review document is empty.");
            }

            Validate(document);
            return document;
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (JsonException ex)
        {
            throw new EvidenceValidationException("AI review document is not valid ai-review-v1 JSON.", ex);
        }
    }

    public static byte[] Serialize(AiReviewDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerContext.AiReviewDocument);
        if (bytes.Length > MaximumDocumentBytes)
        {
            throw new EvidenceValidationException(
                $"AI review exceeds the {MaximumDocumentBytes}-byte boundary.");
        }

        return bytes;
    }

    public static void Validate(AiReviewDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (document.SchemaVersion != 1)
        {
            throw new EvidenceValidationException("AI review schemaVersion must be 1.");
        }
        if (document.Task is not ("compare" or "describe" or "issues"))
        {
            throw new EvidenceValidationException("AI review task must be compare, describe, or issues.");
        }

        ValidateText(document.Provider, "provider", 100);
        ValidateText(document.Model, "model", 200);
        ValidateHash(document.PromptSha256, "promptSha256");
        if (document.TransportMaxEdge is < 1 or > 8192)
        {
            throw new EvidenceValidationException("AI review transportMaxEdge must be between 1 and 8192.");
        }
        if (document.Reviews is null || document.Reviews.Count is < 1 or > MaximumReviewCount)
        {
            throw new EvidenceValidationException(
                $"AI review must contain between 1 and {MaximumReviewCount} review entries.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (AiReviewEntry? review in document.Reviews)
        {
            if (review is null)
            {
                throw new EvidenceValidationException("AI review cannot contain a null review entry.");
            }

            ValidateText(review.Key, "review key", 200);
            if (!keys.Add(review.Key))
            {
                throw new EvidenceValidationException($"AI review contains duplicate key '{review.Key}'.");
            }
            if (review.Source is null)
            {
                throw new EvidenceValidationException($"AI review '{review.Key}' is missing source provenance.");
            }

            ValidateSource(review.Key, review.Source);
            ValidateOptionalText(review.AltText, $"review '{review.Key}' altText", 500);
            ValidateOptionalText(review.Summary, $"review '{review.Key}' summary", 2000);
            ValidateTextCollection(review.Differences, $"review '{review.Key}' differences", MaximumDifferenceCount, 1000);
            ValidateIssues(review.Key, review.Issues);
        }
    }

    private static void ValidateSource(string key, AiReviewSource source)
    {
        bool hasPair = source.BeforeSha256 is not null || source.AfterSha256 is not null;
        bool hasImage = source.ImageSha256 is not null;
        if (hasPair == hasImage || (hasPair && (source.BeforeSha256 is null || source.AfterSha256 is null)))
        {
            throw new EvidenceValidationException(
                $"AI review '{key}' source must contain either beforeSha256 and afterSha256 or imageSha256.");
        }

        if (hasPair)
        {
            ValidateHash(source.BeforeSha256!, $"review '{key}' beforeSha256");
            ValidateHash(source.AfterSha256!, $"review '{key}' afterSha256");
        }
        else
        {
            ValidateHash(source.ImageSha256!, $"review '{key}' imageSha256");
        }
    }

    private static void ValidateIssues(string key, IReadOnlyList<AiReviewIssue>? issues)
    {
        if (issues is null)
        {
            return;
        }
        if (issues.Count > MaximumIssueCount)
        {
            throw new EvidenceValidationException(
                $"AI review '{key}' contains more than {MaximumIssueCount} issues.");
        }

        foreach (AiReviewIssue? issue in issues)
        {
            if (issue is null)
            {
                throw new EvidenceValidationException($"AI review '{key}' cannot contain a null issue.");
            }
            if (issue.Severity is not ("high" or "medium" or "low"))
            {
                throw new EvidenceValidationException(
                    $"AI review '{key}' issue severity must be high, medium, or low.");
            }
            ValidateOptionalText(issue.Area, $"review '{key}' issue area", 200);
            ValidateText(issue.Description, $"review '{key}' issue description", 1000);
        }
    }

    private static void ValidateTextCollection(
        IReadOnlyList<string>? values,
        string name,
        int maximumCount,
        int maximumLength)
    {
        if (values is null)
        {
            return;
        }
        if (values.Count > maximumCount)
        {
            throw new EvidenceValidationException($"{name} contains more than {maximumCount} entries.");
        }
        foreach (string? value in values)
        {
            ValidateText(value, name, maximumLength);
        }
    }

    private static void ValidateOptionalText(string? value, string name, int maximumLength)
    {
        if (value is not null)
        {
            ValidateText(value, name, maximumLength);
        }
    }

    private static void ValidateText(string? value, string name, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.Length > maximumLength ||
            value.Any(static character =>
                char.IsControl(character) ||
                CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.Format))
        {
            throw new EvidenceValidationException(
                $"AI review {name} must contain between 1 and {maximumLength} characters and no control or formatting characters.");
        }
    }

    private static void ValidateHash(string? value, string name)
    {
        if (value is null ||
            value.Length != 64 ||
            value.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new EvidenceValidationException($"AI review {name} must be a 64-character SHA-256 value.");
        }
    }

    private static void RejectDuplicateProperties(ReadOnlySpan<byte> utf8Json)
    {
        var reader = new Utf8JsonReader(utf8Json, new JsonReaderOptions
        {
            CommentHandling = JsonCommentHandling.Disallow,
            MaxDepth = 32,
            AllowTrailingCommas = false,
        });
        var propertySets = new Stack<HashSet<string>>();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.StartObject)
            {
                propertySets.Push(new HashSet<string>(StringComparer.Ordinal));
            }
            else if (reader.TokenType == JsonTokenType.EndObject)
            {
                propertySets.Pop();
            }
            else if (reader.TokenType == JsonTokenType.PropertyName)
            {
                string propertyName = reader.GetString()!;
                if (!propertySets.Peek().Add(propertyName))
                {
                    throw new EvidenceValidationException(
                        $"AI review contains duplicate JSON property '{propertyName}'.");
                }
            }
        }
    }
}
