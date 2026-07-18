// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.Tests;

public sealed class GitHubAssetUrlTests
{
    [Fact]
    public void BuildPinsTheV1BlobUrlShape()
    {
        string commit = new('a', 40);

        string url = GitHubAssetUrl.Build(
            "WoolData/visual-evidence",
            commit,
            "pr-5/head/after/Home compact.png");

        Assert.Equal(
            $"https://github.com/WoolData/visual-evidence/blob/{commit}/pr-5/head/after/Home%20compact.png?raw=true",
            url);
    }

    [Fact]
    public void BuildEvidencePrefixMatchesPublishedUrl()
    {
        string commit = new('a', 40);
        string prefix = GitHubAssetUrl.BuildEvidencePrefix(
            "WoolData/visual-evidence",
            commit,
            5,
            "head",
            "before");
        string url = GitHubAssetUrl.Build(
            "WoolData/visual-evidence",
            commit,
            "pr-5/head/before/home.png");

        Assert.StartsWith(prefix, url, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRepositoryRejectsMalformedName()
    {
        Assert.Throws<ArgumentException>(() => GitHubAssetUrl.ValidateRepository("missing-owner"));
    }
}
