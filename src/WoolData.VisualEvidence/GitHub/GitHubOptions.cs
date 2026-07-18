// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.GitHub;

public sealed record GitHubOptions
{
    public required string Repository { get; init; }

    public required string Token { get; init; }

    public string ApiUrl { get; init; } = "https://api.github.com";

    public string AssetsBranch { get; init; } = "visual-evidence-assets";

    public string? CommentAuthorLogin { get; init; }

    public int MaximumPublishAttempts { get; init; } = 3;
}
