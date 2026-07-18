// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace WoolData.VisualEvidence;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(EvidenceManifest))]
[JsonSerializable(typeof(GitHubCommentPayload))]
[JsonSerializable(typeof(GitHubStatusPayload))]
[JsonSerializable(typeof(GitHubBlobPayload))]
[JsonSerializable(typeof(GitHubTreePayload))]
[JsonSerializable(typeof(GitHubCommitPayload))]
[JsonSerializable(typeof(GitHubRefCreatePayload))]
[JsonSerializable(typeof(GitHubRefUpdatePayload))]
internal sealed partial class VisualEvidenceJsonContext : JsonSerializerContext;

internal sealed record GitHubCommentPayload(string Body);

internal sealed record GitHubStatusPayload(string State, string Description, string Context);

internal sealed record GitHubBlobPayload(string Content, string Encoding);

internal sealed record GitHubTreeEntry(string Path, string Mode, string Type, string Sha);

internal sealed record GitHubTreePayload(
    [property: JsonPropertyName("base_tree")] string? BaseTree,
    IReadOnlyList<GitHubTreeEntry> Tree);

internal sealed record GitHubCommitPayload(string Message, string Tree, IReadOnlyList<string>? Parents);

internal sealed record GitHubRefCreatePayload(
    [property: JsonPropertyName("ref")] string Ref,
    string Sha);

internal sealed record GitHubRefUpdatePayload(string Sha, bool Force);
