// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;

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
        return await BuildAsync(
            snapshot,
            revision,
            captureRoot,
            outputPath,
            environment,
            new EvidenceValidationOptions(),
            cancellationToken).ConfigureAwait(false);
    }

    public static async Task<EvidenceManifest> BuildAsync(
        string snapshot,
        string revision,
        string captureRoot,
        string outputPath,
        CaptureEnvironment environment,
        EvidenceValidationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(options);
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
        string[] files = SafePngFileEnumerator.Enumerate(root, cancellationToken);
        if (files.Length is 0 || files.Length > options.MaximumCaptureCount)
        {
            throw new EvidenceValidationException(
                $"Capture directory must contain between 1 and {options.MaximumCaptureCount} PNG files: {root}");
        }

        var captures = new List<EvidenceCapture>(files.Length);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string relativeToCaptureRoot = Path.GetRelativePath(root, file).Replace('\\', '/');
            string key = CaptureKey.Create(relativeToCaptureRoot, "Capture path");
            if (!keys.Add(key))
            {
                throw new EvidenceValidationException($"Capture paths produce duplicate key '{key}'. Rename one of the files.");
            }
            string path = Path.GetRelativePath(outputDirectory, file).Replace('\\', '/');
            if (path.StartsWith("../", StringComparison.Ordinal))
            {
                throw new EvidenceValidationException("Manifest output must be an ancestor of the capture directory.");
            }
            string label = Path.GetFileNameWithoutExtension(relativeToCaptureRoot).Replace('-', ' ').Replace('_', ' ');
            if (string.IsNullOrWhiteSpace(label) || label.Length > 200)
            {
                throw new EvidenceValidationException($"Capture label must contain between 1 and 200 characters: {file}");
            }
            ValidatedImage image = PngImageValidator.Validate(file, key, label, options);
            captures.Add(new EvidenceCapture
            {
                Key = key,
                Label = label,
                Path = path,
                Width = image.Width,
                Height = image.Height,
                Sha256 = image.SourceSha256,
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

    [GeneratedRegex("^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionRegex();
}
