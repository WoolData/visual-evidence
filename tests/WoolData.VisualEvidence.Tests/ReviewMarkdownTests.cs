// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.Tests;

public sealed class ReviewMarkdownTests
{
    [Fact]
    public void Build_PinsAssetsAndEscapesReviewerControlledText()
    {
        string head = new('2', 40);
        string mergeBase = new('1', 40);
        string assetCommit = new('3', 40);
        var revision = new ChangeRequestRevision(7, head, new string('4', 40), mergeBase);
        var publication = new AssetPublication(assetCommit, new[]
        {
            new PublishedAsset("home", "Home | compact", $"pr-7/{head}/before/home.png", $"pr-7/{head}/after/home.png"),
        });

        string markdown = ReviewMarkdown.Build("WoolData/example", revision, publication, "Changed <layout> | safely");

        Assert.Contains(ReviewMarkdown.Marker, markdown, StringComparison.Ordinal);
        Assert.Contains($"Visual-Evidence-Head: {head}", markdown, StringComparison.Ordinal);
        Assert.Contains($"Visual-Evidence-Base: {mergeBase}", markdown, StringComparison.Ordinal);
        Assert.Contains($"blob/{assetCommit}/pr-7/{head}/before/home.png?raw=true", markdown, StringComparison.Ordinal);
        Assert.Contains("Changed &lt;layout&gt; \\| safely", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("### Home | compact", markdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_EmitsLineFeedOnlyMarkdownOnEveryPlatform()
    {
        string head = new('2', 40);
        var revision = new ChangeRequestRevision(7, head, new string('4', 40), new string('1', 40));
        var publication = new AssetPublication(new string('3', 40), new[]
        {
            new PublishedAsset("home", "Home compact", $"pr-7/{head}/before/home.png", $"pr-7/{head}/after/home.png"),
        });

        string markdown = ReviewMarkdown.Build("WoolData/example", revision, publication, "Summary");

        Assert.DoesNotContain('\r', markdown);
        Assert.Contains('\n', markdown);
    }

    [Fact]
    public void BuildNeutralizesUntrustedMarkdownAndMentions()
    {
        var revision = new ChangeRequestRevision(7, new string('b', 40), new string('a', 40), new string('c', 40));
        var publication = new AssetPublication(
            new string('d', 40),
            [new PublishedAsset("home", "Home\n@WoolData/reviewers | compact", "before/home.png", "after/home.png")]);

        string markdown = ReviewMarkdown.Build(
            "WoolData/visual-evidence",
            revision,
            publication,
            "Changed\r\n@WoolData/reviewers <script>");

        Assert.DoesNotContain("\n@WoolData", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", markdown, StringComparison.Ordinal);
        Assert.Contains("\\| compact", markdown, StringComparison.Ordinal);
        Assert.Contains("&#64;WoolData/reviewers", markdown, StringComparison.Ordinal);
    }
}
