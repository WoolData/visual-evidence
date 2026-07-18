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
        GitHubAssetUrl.ValidateRepository(repository);

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
            string beforeUrl = GitHubAssetUrl.Build(repository, publication.CommitSha, asset.BeforePath);
            string afterUrl = GitHubAssetUrl.Build(repository, publication.CommitSha, asset.AfterPath);
            markdown.AppendLine();
            markdown.AppendLine($"### {Escape(asset.Label)}");
            markdown.AppendLine();
            markdown.AppendLine("| Before | After |");
            markdown.AppendLine("|---|---|");
            markdown.AppendLine($"| ![Before: {EscapeAlt(asset.Label)}]({beforeUrl}) | ![After: {EscapeAlt(asset.Label)}]({afterUrl}) |");
        }

        // StringBuilder.AppendLine emits the platform newline, and GitHub preserves posted
        // CRLF bytes verbatim. Normalize so published markdown is byte-identical on every
        // operating system and marker lines stay matchable by line-anchored verification.
        return markdown.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
    }

    public static string BuildImages(
        string repository,
        ChangeRequestRevision revision,
        ImageAssetPublication publication,
        string summary)
    {
        GitHubAssetUrl.ValidateRepository(repository);

        var markdown = new StringBuilder();
        AppendHeader(markdown, revision, publication.CommitSha, summary);
        foreach (PublishedImageAsset asset in publication.Assets)
        {
            string url = GitHubAssetUrl.Build(repository, publication.CommitSha, asset.Path);
            markdown.AppendLine();
            markdown.AppendLine($"### {Escape(asset.Label)}");
            markdown.AppendLine();
            markdown.AppendLine($"![{EscapeAlt(asset.Label)}]({url})");
        }
        return Normalize(markdown);
    }

    private static void AppendHeader(
        StringBuilder markdown,
        ChangeRequestRevision revision,
        string commitSha,
        string summary)
    {
        markdown.AppendLine(Marker);
        markdown.AppendLine($"Visual-Evidence-Head: {revision.HeadRevision}");
        markdown.AppendLine($"Visual-Evidence-Base: {revision.MergeBaseRevision}");
        markdown.AppendLine($"Visual-Evidence-Asset-Commit: {commitSha}");
        markdown.AppendLine();
        markdown.AppendLine("## Visual evidence");
        markdown.AppendLine();
        markdown.AppendLine(Escape(summary));
        markdown.AppendLine();
        markdown.AppendLine(
            $"Captured for head `{revision.HeadRevision}` against merge base `{revision.MergeBaseRevision}`. " +
            $"Assets are pinned to repository commit `{commitSha}`.");
    }

    private static string Normalize(StringBuilder markdown) =>
        markdown.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

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
