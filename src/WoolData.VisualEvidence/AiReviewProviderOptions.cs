// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public sealed record AnthropicImageReviewOptions
{
    public required string ApiKey { get; init; }

    public required string Model { get; init; }

    public Uri BaseUri { get; init; } = new("https://api.anthropic.com/");

    public int MaximumOutputTokens { get; init; } = 4096;
}

public sealed record OpenAiCompatibleImageReviewOptions
{
    public string? ApiKey { get; init; }

    public required string Model { get; init; }

    public string ProviderName { get; init; } = "openai-compatible";

    public Uri BaseUri { get; init; } = new("https://api.openai.com/v1/");
}
