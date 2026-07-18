// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace WoolData.VisualEvidence;

public static class EvidenceManifestReader
{
    private const long MaximumManifestBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly VisualEvidenceJsonContext SerializerContext = new(SerializerOptions);

    public static async Task<EvidenceManifest> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length is <= 0 or > MaximumManifestBytes)
            {
                throw new EvidenceValidationException(
                    $"Manifest must be present and no larger than {MaximumManifestBytes} bytes: {path}");
            }

            await using FileStream stream = File.OpenRead(path);
            EvidenceManifest? manifest = await JsonSerializer.DeserializeAsync(
                stream,
                SerializerContext.EvidenceManifest,
                cancellationToken).ConfigureAwait(false);
            if (manifest is null)
            {
                throw new EvidenceValidationException($"Manifest is empty: {path}");
            }
            if (manifest.Environment is null || manifest.Captures is null || manifest.Captures.Any(static capture => capture is null))
            {
                throw new EvidenceValidationException($"Manifest contains a null environment, capture collection, or capture: {path}");
            }
            return manifest;
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new EvidenceValidationException($"Could not read evidence manifest '{path}'.", ex);
        }
    }
}
