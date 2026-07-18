// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace WoolData.VisualEvidence;

public sealed partial class EvidenceImageSetValidator
{
    private readonly EvidenceValidationOptions _options;
    private readonly string? _imageRoot;

    public EvidenceImageSetValidator(EvidenceValidationOptions? options = null, string? imageRoot = null)
    {
        _options = options ?? new EvidenceValidationOptions();
        _imageRoot = imageRoot is null ? null : Path.GetFullPath(imageRoot);
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
            string keySource = _imageRoot is null ? Path.GetFileName(path) : RelativePath(path);
            string key = CreateKey(keySource);
            if (!keys.Add(key))
            {
                throw new EvidenceValidationException(
                    $"Image paths produce duplicate key '{key}'. Rename one of the files.");
            }
            images.Add(PngImageValidator.Validate(path, key, CreateLabel(keySource), _options));
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
        return new EvidenceImageSetValidator(_options, root).ValidateAsync(files, cancellationToken);
    }

    private string RelativePath(string path)
    {
        string relative = Path.GetRelativePath(_imageRoot!, path);
        if (relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            throw new EvidenceValidationException($"Image escapes the configured image directory: {path}");
        }
        return relative;
    }

    private static string CreateKey(string path)
    {
        string key = InvalidKeyCharacterRegex().Replace(Path.ChangeExtension(path, null)!, "-")
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
        Path.ChangeExtension(path, null)!
            .Replace('\\', '/')
            .Replace("/", " / ", StringComparison.Ordinal)
            .Replace('-', ' ')
            .Replace('_', ' ');

    private static StringComparer PathComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    [GeneratedRegex("[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidKeyCharacterRegex();
}
