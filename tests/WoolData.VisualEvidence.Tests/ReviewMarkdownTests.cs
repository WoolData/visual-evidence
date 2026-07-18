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

    [Fact]
    public void BuildImages_PublishesStandaloneImagesWithoutInventingComparisonSemantics()
    {
        string head = new('2', 40);
        string assetCommit = new('3', 40);
        var revision = new ChangeRequestRevision(7, head, new string('4', 40), new string('1', 40));
        var publication = new ImageAssetPublication(assetCommit,
        [
            new PublishedImageAsset("step-1", "Step 1", $"pr-7/{head}/images/step-1.png"),
        ]);

        string markdown = ReviewMarkdown.BuildImages("WoolData/example", revision, publication, "Current UI");

        Assert.Contains("## Visual evidence", markdown, StringComparison.Ordinal);
        Assert.Contains($"blob/{assetCommit}/pr-7/{head}/images/step-1.png?raw=true", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| Before | After |", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', markdown);
    }

    [Fact]
    public void Build_WithAiReviewPublishesPinnedDigestAndUsefulAltText()
    {
        string head = new('2', 40);
        string assetCommit = new('3', 40);
        var revision = new ChangeRequestRevision(7, head, new string('4', 40), new string('1', 40));
        var publication = new AssetPublication(
            assetCommit,
            [new PublishedAsset("home", "Home", $"pr-7/{head}/before/home.png", $"pr-7/{head}/after/home.png")],
            $"pr-7/{head}/ai-review-v1.json");
        AiReviewDocument review = AiReviewDocumentCodecTests.CreateDocument() with
        {
            Provider = "provider\u0060@reviewers",
            Model = "model",
            Reviews =
            [
                AiReviewDocumentCodecTests.CreateDocument().Reviews.Single() with
                {
                    Key = "home",
                    AltText = "Settings screen | after save action moved",
                    Summary = "Save moved below the form. @reviewers See https://evil.example/path.",
                    Issues =
                    [
                        new AiReviewIssue
                        {
                            Severity = "medium",
                            Area = "footer",
                            Description = "Status text is close to the edge.",
                        },
                        new AiReviewIssue
                        {
                            Severity = "low",
                            Area = "color",
                            Description = "Minor color difference.",
                        },
                    ],
                },
            ],
        };

        string markdown = ReviewMarkdown.Build("WoolData/example", revision, publication, "Summary", review);

        Assert.Contains("## Advisory AI visual review", markdown, StringComparison.Ordinal);
        Assert.Contains("Machine-generated observations only", markdown, StringComparison.Ordinal);
        Assert.Contains($"blob/{assetCommit}/pr-7/{head}/ai-review-v1.json?raw=true", markdown, StringComparison.Ordinal);
        Assert.Contains("Settings screen \\| after save action moved", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("| ![Settings screen |", markdown, StringComparison.Ordinal);
        Assert.Contains("**MEDIUM** footer:", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Minor color difference", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("@reviewers", markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("https://evil.example", markdown, StringComparison.Ordinal);
        Assert.Contains("hxxps://evil.example", markdown, StringComparison.Ordinal);
        Assert.Contains("&#96;&#64;reviewers", markdown, StringComparison.Ordinal);
    }
}
