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
            EvidenceManifest? manifest = await JsonSerializer.DeserializeAsync<EvidenceManifest>(
                stream,
                SerializerOptions,
                cancellationToken).ConfigureAwait(false);
            return manifest ?? throw new EvidenceValidationException($"Manifest is empty: {path}");
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
