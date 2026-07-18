// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace WoolData.VisualEvidence;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiReviewDocument
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("task")]
    public required string Task { get; init; }

    [JsonPropertyName("provider")]
    public required string Provider { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("promptSha256")]
    public required string PromptSha256 { get; init; }

    [JsonPropertyName("transportMaxEdge")]
    public int TransportMaxEdge { get; init; }

    [JsonPropertyName("reviews")]
    public required IReadOnlyList<AiReviewEntry> Reviews { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiReviewEntry
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("source")]
    public required AiReviewSource Source { get; init; }

    [JsonPropertyName("altText")]
    public string? AltText { get; init; }

    [JsonPropertyName("summary")]
    public string? Summary { get; init; }

    [JsonPropertyName("differences")]
    public IReadOnlyList<string>? Differences { get; init; }

    [JsonPropertyName("issues")]
    public IReadOnlyList<AiReviewIssue>? Issues { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiReviewSource
{
    [JsonPropertyName("beforeSha256")]
    public string? BeforeSha256 { get; init; }

    [JsonPropertyName("afterSha256")]
    public string? AfterSha256 { get; init; }

    [JsonPropertyName("imageSha256")]
    public string? ImageSha256 { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AiReviewIssue
{
    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("area")]
    public string? Area { get; init; }

    [JsonPropertyName("description")]
    public required string Description { get; init; }
}

public sealed record AiReviewRequest(
    string Task,
    string Prompt,
    string PromptSha256,
    int TransportMaxEdge,
    IReadOnlyList<AiReviewTransportPair> Captures);

public sealed record AiReviewTransportPair(
    string Key,
    string Label,
    AiReviewTransportImage Before,
    AiReviewTransportImage After);

public sealed record AiReviewTransportImage(
    string SourceSha256,
    int Width,
    int Height,
    byte[] PngBytes);
