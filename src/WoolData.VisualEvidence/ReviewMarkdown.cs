// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text;

namespace WoolData.VisualEvidence;

public static class ReviewMarkdown
{
    public const string Marker = "<!-- wooldata-visual-evidence:v1 -->";
    private const int MaximumAiDigestCharacters = 8000;

    public static string Build(
        string repository,
        ChangeRequestRevision revision,
        AssetPublication publication,
        string summary,
        AiReviewDocument? aiReview = null)
    {
        GitHubAssetUrl.ValidateRepository(repository);
        if (aiReview is not null)
        {
            AiReviewDocumentCodec.Validate(aiReview);
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

        IReadOnlyDictionary<string, AiReviewEntry> aiByKey = aiReview?.Reviews.ToDictionary(
            static review => review.Key,
            StringComparer.Ordinal) ?? new Dictionary<string, AiReviewEntry>(StringComparer.Ordinal);
        if (aiReview is not null)
        {
            AppendAiReview(markdown, repository, publication, aiReview);
        }

        foreach (PublishedAsset asset in publication.Assets)
        {
            string beforeUrl = GitHubAssetUrl.Build(repository, publication.CommitSha, asset.BeforePath);
            string afterUrl = GitHubAssetUrl.Build(repository, publication.CommitSha, asset.AfterPath);
            string afterAlt = aiByKey.TryGetValue(asset.Key, out AiReviewEntry? review) &&
                !string.IsNullOrWhiteSpace(review.AltText)
                    ? review.AltText
                    : $"After: {asset.Label}";
            markdown.AppendLine();
            markdown.AppendLine($"### {Escape(asset.Label)}");
            markdown.AppendLine();
            markdown.AppendLine("| Before | After |");
            markdown.AppendLine("|---|---|");
            markdown.AppendLine($"| ![Before: {EscapeAlt(asset.Label)}]({beforeUrl}) | ![{EscapeAlt(afterAlt)}]({afterUrl}) |");
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

    private static void AppendAiReview(
        StringBuilder markdown,
        string repository,
        AssetPublication publication,
        AiReviewDocument review)
    {
        if (string.IsNullOrWhiteSpace(publication.AiReviewPath))
        {
            throw new ArgumentException("AI review publication is missing its durable review path.");
        }

        string fullReviewUrl = GitHubAssetUrl.Build(repository, publication.CommitSha, publication.AiReviewPath);
        var digest = new StringBuilder();
        digest.AppendLine();
        digest.AppendLine("## Advisory AI visual review");
        digest.AppendLine();
        digest.AppendLine("Machine-generated observations only; they are not an approval or merge gate.");
        digest.AppendLine();
        digest.AppendLine(
            $"Provider '{EscapeAi(review.Provider)}', model '{EscapeAi(review.Model)}', prompt '{review.PromptSha256[..12]}'. " +
            $"[Full structured review]({fullReviewUrl}) is pinned to this evidence commit.");

        foreach (AiReviewEntry entry in review.Reviews)
        {
            var section = new StringBuilder();
            section.AppendLine();
            section.AppendLine($"### {EscapeAi(entry.Key)} review");
            if (!string.IsNullOrWhiteSpace(entry.Summary))
            {
                section.AppendLine();
                section.AppendLine(EscapeAi(entry.Summary));
            }
            foreach (AiReviewIssue issue in entry.Issues ?? Array.Empty<AiReviewIssue>())
            {
                if (issue.Severity is not ("high" or "medium"))
                {
                    continue;
                }
                section.AppendLine();
                string area = string.IsNullOrWhiteSpace(issue.Area)
                    ? string.Empty
                    : $"{EscapeAi(issue.Area)}: ";
                section.AppendLine(
                    $"- **{EscapeAi(issue.Severity.ToUpperInvariant())}** {area}{EscapeAi(issue.Description)}");
            }

            if (digest.Length + section.Length > MaximumAiDigestCharacters)
            {
                digest.AppendLine();
                digest.AppendLine("Additional observations are available in the full structured review.");
                break;
            }
            digest.Append(section);
        }

        markdown.Append(digest);
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
        .Replace("\u0060", "&#96;", StringComparison.Ordinal)
        .Replace("@", "&#64;", StringComparison.Ordinal);

    private static string EscapeAlt(string value) => NormalizeSingleLine(value)
        .Replace("[", string.Empty, StringComparison.Ordinal)
        .Replace("]", string.Empty, StringComparison.Ordinal)
        .Replace("(", string.Empty, StringComparison.Ordinal)
        .Replace(")", string.Empty, StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("@", "&#64;", StringComparison.Ordinal);

    private static string EscapeAi(string value) => Escape(value)
        .Replace("https://", "hxxps://", StringComparison.OrdinalIgnoreCase)
        .Replace("http://", "hxxp://", StringComparison.OrdinalIgnoreCase)
        .Replace("www.", "www[.]", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSingleLine(string value) => string.Join(
        ' ',
        value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
