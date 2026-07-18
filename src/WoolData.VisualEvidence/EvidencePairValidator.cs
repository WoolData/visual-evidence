// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace WoolData.VisualEvidence;

public sealed partial class EvidencePairValidator
{
    private readonly EvidenceValidationOptions _options;

    public EvidencePairValidator(EvidenceValidationOptions? options = null)
    {
        _options = options ?? new EvidenceValidationOptions();
    }

    public async Task<ValidatedEvidencePair> ValidateAsync(
        string evidenceRoot,
        string? expectedBaseRevision = null,
        string? expectedHeadRevision = null,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(evidenceRoot);
        EvidenceManifest before = await EvidenceManifestReader.ReadAsync(
            Path.Combine(root, "before", "manifest.json"),
            cancellationToken).ConfigureAwait(false);
        EvidenceManifest after = await EvidenceManifestReader.ReadAsync(
            Path.Combine(root, "after", "manifest.json"),
            cancellationToken).ConfigureAwait(false);

        ValidateManifest(before, "before", expectedBaseRevision);
        ValidateManifest(after, "after", expectedHeadRevision);
        ValidateEnvironment(before.Environment, after.Environment);

        Dictionary<string, EvidenceCapture> beforeByKey = IndexCaptures(before);
        Dictionary<string, EvidenceCapture> afterByKey = IndexCaptures(after);
        if (!beforeByKey.Keys.Order().SequenceEqual(afterByKey.Keys.Order(), StringComparer.Ordinal))
        {
            throw new EvidenceValidationException("Before and after manifests must contain the same capture keys.");
        }

        var pairs = new List<ValidatedImagePair>(beforeByKey.Count);
        foreach (string key in beforeByKey.Keys.Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EvidenceCapture beforeCapture = beforeByKey[key];
            EvidenceCapture afterCapture = afterByKey[key];
            if (!string.Equals(beforeCapture.Label, afterCapture.Label, StringComparison.Ordinal))
            {
                throw new EvidenceValidationException($"Capture '{key}' has different before and after labels.");
            }

            ValidatedImage beforeImage = ValidateImage(root, "before", beforeCapture);
            ValidatedImage afterImage = ValidateImage(root, "after", afterCapture);
            pairs.Add(new ValidatedImagePair(key, beforeCapture.Label, beforeImage, afterImage));
        }

        return new ValidatedEvidencePair(before, after, pairs);
    }

    private void ValidateManifest(EvidenceManifest manifest, string expectedSnapshot, string? expectedRevision)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw new EvidenceValidationException($"Unsupported manifest schema version: {manifest.SchemaVersion}.");
        }
        if (!string.Equals(manifest.Snapshot, expectedSnapshot, StringComparison.Ordinal))
        {
            throw new EvidenceValidationException($"Expected a '{expectedSnapshot}' manifest, received '{manifest.Snapshot}'.");
        }
        if (string.IsNullOrWhiteSpace(manifest.Revision) || !RevisionRegex().IsMatch(manifest.Revision))
        {
            throw new EvidenceValidationException($"Manifest revision is not a full Git object ID: {manifest.Revision}");
        }
        if (expectedRevision is not null &&
            !string.Equals(manifest.Revision, expectedRevision, StringComparison.OrdinalIgnoreCase))
        {
            throw new EvidenceValidationException(
                $"The {expectedSnapshot} manifest is stale. Expected {expectedRevision}; received {manifest.Revision}.");
        }
        if (manifest.Captures.Count is 0 || manifest.Captures.Count > _options.MaximumCaptureCount)
        {
            throw new EvidenceValidationException(
                $"The {expectedSnapshot} manifest must contain between 1 and {_options.MaximumCaptureCount} captures.");
        }
    }

    private static void ValidateEnvironment(CaptureEnvironment before, CaptureEnvironment after)
    {
        ValidateEnvironmentFields(before);
        ValidateEnvironmentFields(after);
        string calculatedBefore = before.CalculateCompatibilityKey();
        string calculatedAfter = after.CalculateCompatibilityKey();
        if (!NormalizedHashEquals(before.CompatibilityKey, calculatedBefore) ||
            !NormalizedHashEquals(after.CompatibilityKey, calculatedAfter))
        {
            throw new EvidenceValidationException("A capture environment compatibility key does not match its fields.");
        }
        if (!NormalizedHashEquals(calculatedBefore, calculatedAfter))
        {
            throw new EvidenceValidationException(
                "Before and after captures were produced by incompatible operating systems, runners, renderers, scales, adapters, or font sets.");
        }
    }

    private static void ValidateEnvironmentFields(CaptureEnvironment environment)
    {
        string[] values =
        [
            environment.OperatingSystem,
            environment.Architecture,
            environment.RunnerImage,
            environment.CaptureAdapter,
            environment.AdapterVersion,
            environment.Renderer,
            environment.FontSetHash,
            environment.CompatibilityKey,
        ];
        if (values.Any(string.IsNullOrWhiteSpace) || values.Any(value => value.Length > 200) ||
            !double.IsFinite(environment.RenderScale) || environment.RenderScale <= 0)
        {
            throw new EvidenceValidationException("Capture environment fields are incomplete or invalid.");
        }
    }

    private static Dictionary<string, EvidenceCapture> IndexCaptures(EvidenceManifest manifest)
    {
        var captures = new Dictionary<string, EvidenceCapture>(StringComparer.Ordinal);
        foreach (EvidenceCapture capture in manifest.Captures)
        {
            if (string.IsNullOrWhiteSpace(capture.Key) || !CaptureKeyRegex().IsMatch(capture.Key))
            {
                throw new EvidenceValidationException($"Capture key contains unsupported syntax: {capture.Key}");
            }
            if (string.IsNullOrWhiteSpace(capture.Label) || capture.Label.Length > 200)
            {
                throw new EvidenceValidationException($"Capture '{capture.Key}' must have a label of 1-200 characters.");
            }
            if (!captures.TryAdd(capture.Key, capture))
            {
                throw new EvidenceValidationException($"Duplicate capture key: {capture.Key}");
            }
        }
        return captures;
    }

    private ValidatedImage ValidateImage(string root, string snapshot, EvidenceCapture capture)
    {
        if (string.IsNullOrWhiteSpace(capture.Path) || Path.IsPathRooted(capture.Path) || capture.Path.Contains('\0'))
        {
            throw new EvidenceValidationException($"Capture '{capture.Key}' must use a relative path.");
        }

        string snapshotRoot = Path.GetFullPath(Path.Combine(root, snapshot));
        string path = Path.GetFullPath(Path.Combine(snapshotRoot, capture.Path));
        string prefix = snapshotRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!path.StartsWith(prefix, comparison))
        {
            throw new EvidenceValidationException($"Capture '{capture.Key}' escapes the {snapshot} evidence directory.");
        }

        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new EvidenceValidationException($"Capture '{capture.Key}' is missing: {path}");
        }
        if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new EvidenceValidationException($"Capture '{capture.Key}' cannot be a symbolic link or reparse point.");
        }
        DirectoryInfo? ancestor = file.Directory;
        while (ancestor is not null)
        {
            if (ancestor.LinkTarget is not null || ancestor.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new EvidenceValidationException($"Capture '{capture.Key}' cannot traverse a symbolic link or reparse point.");
            }
            if (string.Equals(ancestor.FullName, snapshotRoot, comparison))
            {
                break;
            }
            ancestor = ancestor.Parent;
        }
        if (ancestor is null)
        {
            throw new EvidenceValidationException($"Capture '{capture.Key}' could not be traced to its snapshot directory.");
        }
        return PngImageValidator.Validate(
            path,
            capture.Key,
            capture.Label,
            _options,
            capture.Width,
            capture.Height,
            capture.Sha256);
    }

    private static bool NormalizedHashEquals(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.-]{0,159}$", RegexOptions.CultureInvariant)]
    private static partial Regex CaptureKeyRegex();

    [GeneratedRegex("^(?:[0-9a-fA-F]{40}|[0-9a-fA-F]{64})$", RegexOptions.CultureInvariant)]
    private static partial Regex RevisionRegex();
}
