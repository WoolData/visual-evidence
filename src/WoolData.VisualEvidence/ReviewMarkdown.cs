// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text;

namespace WoolData.VisualEvidence;

public static class ReviewMarkdown
{
    public const string Marker = "<!-- wooldata-visual-evidence:v1 -->";

    public static string Build(
        string repository,
        ChangeRequestRevision revision,
        AssetPublication publication,
        string summary)
    {
        string[] repositoryParts = repository.Split('/');
        if (repositoryParts.Length != 2)
        {
            throw new ArgumentException("Repository must use owner/name form.", nameof(repository));
        }

        var markdown = new StringBuilder();
        markdown.AppendLine(Marker);
        markdown.AppendLine($"Visual-Evidence-Head: {revision.HeadRevision}");
        markdown.AppendLine($"Visual-Evidence-Base: {revision.MergeBaseRevision}");
        markdown.AppendLine($"Visual-Evidence-Asset-Commit: {publication.CommitSha}");
        markdown.AppendLine();
        markdown.AppendLine("## Visual evidence");
        markdown.AppendLine();
        markdown.AppendLine(Escape(summary));
        markdown.AppendLine();
        markdown.AppendLine(
            $"Captured against merge base `{revision.MergeBaseRevision}` and head `{revision.HeadRevision}`. " +
            $"Assets are pinned to repository commit `{publication.CommitSha}`.");

        foreach (PublishedAsset asset in publication.Assets)
        {
            string beforeUrl = BuildAssetUrl(repositoryParts[0], repositoryParts[1], publication.CommitSha, asset.BeforePath);
            string afterUrl = BuildAssetUrl(repositoryParts[0], repositoryParts[1], publication.CommitSha, asset.AfterPath);
            markdown.AppendLine();
            markdown.AppendLine($"### {Escape(asset.Label)}");
            markdown.AppendLine();
            markdown.AppendLine("| Before | After |");
            markdown.AppendLine("|---|---|");
            markdown.AppendLine($"| ![Before: {EscapeAlt(asset.Label)}]({beforeUrl}) | ![After: {EscapeAlt(asset.Label)}]({afterUrl}) |");
        }

        return markdown.ToString().TrimEnd();
    }

    private static string BuildAssetUrl(string owner, string name, string commit, string path) =>
        $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/blob/{commit}/{EscapePath(path)}?raw=true";

    private static string EscapePath(string path) => string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    private static string Escape(string value) => NormalizeSingleLine(value)
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("[", "\\[", StringComparison.Ordinal)
        .Replace("]", "\\]", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("@", "&#64;", StringComparison.Ordinal);

    private static string EscapeAlt(string value) => NormalizeSingleLine(value)
        .Replace("[", string.Empty, StringComparison.Ordinal)
        .Replace("]", string.Empty, StringComparison.Ordinal)
        .Replace("(", string.Empty, StringComparison.Ordinal)
        .Replace(")", string.Empty, StringComparison.Ordinal)
        .Replace("@", "&#64;", StringComparison.Ordinal);

    private static string NormalizeSingleLine(string value) => string.Join(
        ' ',
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
