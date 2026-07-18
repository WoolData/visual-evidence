// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace WoolData.VisualEvidence;

public static partial class EvidenceManifestBuilder
{
    public static async Task<EvidenceManifest> BuildAsync(
        string snapshot,
        string revision,
        string captureRoot,
        string outputPath,
        CaptureEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        if (snapshot is not ("before" or "after"))
        {
            throw new ArgumentException("Snapshot must be 'before' or 'after'.", nameof(snapshot));
        }
        if (!RevisionRegex().IsMatch(revision))
        {
            throw new ArgumentException("Revision must be a full 40- or 64-character Git object ID.", nameof(revision));
        }

        string root = Path.GetFullPath(captureRoot);
        string output = Path.GetFullPath(outputPath);
        string outputDirectory = Path.GetDirectoryName(output)
            ?? throw new ArgumentException("Output path must have a parent directory.", nameof(outputPath));
        string[] files = Directory.GetFiles(root, "*.png", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive,
        });
        if (files.Length == 0)
        {
            throw new EvidenceValidationException($"Capture directory contains no PNG files: {root}");
        }

        var captures = new List<EvidenceCapture>(files.Length);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new FileInfo(file);
            if (fileInfo.LinkTarget is not null || fileInfo.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new EvidenceValidationException($"Capture cannot be a symbolic link or reparse point: {file}");
            }
            if (fileInfo.Length is <= 0 or > (10 * 1024 * 1024))
            {
                throw new EvidenceValidationException($"Capture exceeds the 10 MiB manifest-generation boundary: {file}");
            }
            byte[] bytes = await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false);
            using var data = SKData.CreateCopy(bytes);
            using SKCodec? codec = SKCodec.Create(data);
            if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png)
            {
                throw new EvidenceValidationException($"Capture is not a decodable PNG: {file}");
            }

            string relativeToCaptureRoot = Path.GetRelativePath(root, file).Replace('\\', '/');
            string key = ToKey(relativeToCaptureRoot);
            if (!keys.Add(key))
            {
                throw new EvidenceValidationException($"Capture paths produce duplicate key '{key}'. Rename one of the files.");
            }
            string path = Path.GetRelativePath(outputDirectory, file).Replace('\\', '/');
            if (path.StartsWith("../", StringComparison.Ordinal))
            {
                throw new EvidenceValidationException("Manifest output must be an ancestor of the capture directory.");
            }
            captures.Add(new EvidenceCapture
            {
                Key = key,
                Label = Path.GetFileNameWithoutExtension(relativeToCaptureRoot).Replace('-', ' ').Replace('_', ' '),
                Path = path,
                Width = codec.Info.Width,
                Height = codec.Info.Height,
                Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            });
        }

        CaptureEnvironment completedEnvironment = environment with
        {
            CompatibilityKey = environment.CalculateCompatibilityKey(),
        };
        var manifest = new EvidenceManifest
        {
            SchemaVersion = 1,
            Snapshot = snapshot,
            Revision = revision.ToLowerInvariant(),
            Environment = completedEnvironment,
            Captures = captures,
        };
        Directory.CreateDirectory(outputDirectory);
        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(
                manifest,
                new VisualEvidenceJsonContext(new JsonSerializerOptions { WriteIndented = true }).EvidenceManifest),
            cancellationToken).ConfigureAwait(false);
        return manifest;
    }

    private static string ToKey(string relativePath)
    {
        string withoutExtension = relativePath[..^Path.GetExtension(relativePath).Length];
        string key = InvalidKeyCharacterRegex().Replace(withoutExtension, "-").Trim('-').ToLowerInvariant();
        return key.Length switch
        {
            0 => throw new EvidenceValidationException($"Capture path cannot produce a stable key: {relativePath}"),
            > 160 => throw new EvidenceValidationException($"Capture key exceeds 160 characters: {relativePath}"),
            _ => key,
        };
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidKeyCharacterRegex();

    [GeneratedRegex("^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionRegex();
}
