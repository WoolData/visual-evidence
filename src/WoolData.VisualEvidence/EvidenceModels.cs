// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace WoolData.VisualEvidence;

public sealed record EvidenceManifest
{
    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonPropertyName("snapshot")]
    public required string Snapshot { get; init; }

    [JsonPropertyName("revision")]
    public required string Revision { get; init; }

    [JsonPropertyName("environment")]
    public required CaptureEnvironment Environment { get; init; }

    [JsonPropertyName("captures")]
    public required IReadOnlyList<EvidenceCapture> Captures { get; init; }
}

public sealed record CaptureEnvironment
{
    [JsonPropertyName("os")]
    public required string OperatingSystem { get; init; }

    [JsonPropertyName("architecture")]
    public required string Architecture { get; init; }

    [JsonPropertyName("runnerImage")]
    public required string RunnerImage { get; init; }

    [JsonPropertyName("captureAdapter")]
    public required string CaptureAdapter { get; init; }

    [JsonPropertyName("adapterVersion")]
    public required string AdapterVersion { get; init; }

    [JsonPropertyName("renderer")]
    public required string Renderer { get; init; }

    [JsonPropertyName("renderScale")]
    public double RenderScale { get; init; }

    [JsonPropertyName("fontSetHash")]
    public required string FontSetHash { get; init; }

    [JsonPropertyName("compatibilityKey")]
    public required string CompatibilityKey { get; init; }

    public string CalculateCompatibilityKey()
    {
        string canonical = string.Join(
            "\n",
            Normalize(OperatingSystem),
            Normalize(Architecture),
            Normalize(RunnerImage),
            Normalize(CaptureAdapter),
            Normalize(AdapterVersion),
            Normalize(Renderer),
            RenderScale.ToString("R", CultureInfo.InvariantCulture),
            Normalize(FontSetHash));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed record EvidenceCapture
{
    [JsonPropertyName("key")]
    public required string Key { get; init; }

    [JsonPropertyName("label")]
    public required string Label { get; init; }

    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("width")]
    public int Width { get; init; }

    [JsonPropertyName("height")]
    public int Height { get; init; }

    [JsonPropertyName("sha256")]
    public required string Sha256 { get; init; }
}

public sealed record ValidatedImage(
    string Key,
    string Label,
    string SourcePath,
    int Width,
    int Height,
    string SourceSha256,
    byte[] NormalizedPng);

public sealed record ValidatedEvidencePair(
    EvidenceManifest BeforeManifest,
    EvidenceManifest AfterManifest,
    IReadOnlyList<ValidatedImagePair> Captures);

public sealed record ValidatedImagePair(
    string Key,
    string Label,
    ValidatedImage Before,
    ValidatedImage After);

public sealed record ChangeRequestRevision(
    int Number,
    string HeadRevision,
    string BaseRevision,
    string MergeBaseRevision);

public sealed record PublishedAsset(
    string Key,
    string Label,
    string BeforePath,
    string AfterPath);

public sealed record AssetPublication(
    string CommitSha,
    IReadOnlyList<PublishedAsset> Assets);
