// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace WoolData.VisualEvidence;

public sealed partial class EvidenceImageSetValidator
{
    private readonly EvidenceValidationOptions _options;

    public EvidenceImageSetValidator(EvidenceValidationOptions? options = null)
    {
        _options = options ?? new EvidenceValidationOptions();
    }

    public Task<ValidatedImageSet> ValidateAsync(
        IEnumerable<string> imagePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(imagePaths);
        string[] paths = imagePaths.Select(Path.GetFullPath).Distinct(PathComparer).Order(PathComparer).ToArray();
        if (paths.Length is 0 || paths.Length > _options.MaximumCaptureCount)
        {
            throw new EvidenceValidationException(
                $"Image input must contain between 1 and {_options.MaximumCaptureCount} PNG files.");
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var images = new List<ValidatedImage>(paths.Length);
        foreach (string path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string key = CreateKey(path);
            if (!keys.Add(key))
            {
                throw new EvidenceValidationException(
                    $"Image paths produce duplicate key '{key}'. Rename one of the files.");
            }
            images.Add(PngImageValidator.Validate(path, key, CreateLabel(path), _options));
        }

        return Task.FromResult(new ValidatedImageSet(images));
    }

    public Task<ValidatedImageSet> ValidateDirectoryAsync(
        string imageRoot,
        CancellationToken cancellationToken = default)
    {
        string root = Path.GetFullPath(imageRoot);
        if (!Directory.Exists(root))
        {
            throw new EvidenceValidationException($"Image directory does not exist: {root}");
        }
        string[] files = Directory.GetFiles(root, "*.png", new EnumerationOptions
        {
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive,
        });
        return ValidateAsync(files, cancellationToken);
    }

    private static string CreateKey(string path)
    {
        string key = InvalidKeyCharacterRegex().Replace(Path.GetFileNameWithoutExtension(path), "-")
            .Trim('-')
            .ToLowerInvariant();
        return key.Length switch
        {
            0 => throw new EvidenceValidationException($"Image path cannot produce a stable key: {path}"),
            > 160 => throw new EvidenceValidationException($"Image key exceeds 160 characters: {path}"),
            _ => key,
        };
    }

    private static string CreateLabel(string path) =>
        Path.GetFileNameWithoutExtension(path).Replace('-', ' ').Replace('_', ' ');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    [GeneratedRegex("[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidKeyCharacterRegex();
}
