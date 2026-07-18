// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace WoolData.VisualEvidence;

internal static partial class CaptureKey
{
    public static string Create(string path, string subject)
    {
        string withoutExtension = Path.ChangeExtension(path, null)
            ?? throw new EvidenceValidationException($"{subject} cannot produce a stable key: {path}");
        string key = InvalidCharacterRegex().Replace(withoutExtension, "-")
            .Trim('-', '_', '.')
            .ToLowerInvariant();
        return key.Length switch
        {
            0 => throw new EvidenceValidationException($"{subject} cannot produce a stable key: {path}"),
            > 160 => throw new EvidenceValidationException($"{subject} produces a key longer than 160 characters: {path}"),
            _ => key,
        };
    }

    [GeneratedRegex("[^A-Za-z0-9_.-]+", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidCharacterRegex();
}
