// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace WoolData.VisualEvidence.Tests;

internal sealed class EvidenceFixture : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public EvidenceFixture(bool singleColor = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "visual-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        BeforeRevision = new string('1', 40);
        AfterRevision = new string('2', 40);
        Environment = CreateEnvironment("test-fonts");
        WriteSnapshot("before", BeforeRevision, Environment, singleColor);
        WriteSnapshot("after", AfterRevision, Environment, singleColor);
    }

    public string Root { get; }

    public string BeforeRevision { get; }

    public string AfterRevision { get; }

    public CaptureEnvironment Environment { get; }

    public static CaptureEnvironment CreateEnvironment(string fontHash)
    {
        var environment = new CaptureEnvironment
        {
            OperatingSystem = "test-os",
            Architecture = "x64",
            RunnerImage = "test-runner-v1",
            CaptureAdapter = "test-adapter",
            AdapterVersion = "1.0.0",
            Renderer = "skia",
            RenderScale = 1,
            FontSetHash = fontHash,
            CompatibilityKey = string.Empty,
        };
        return environment with { CompatibilityKey = environment.CalculateCompatibilityKey() };
    }

    public void ReplaceEnvironment(string snapshot, CaptureEnvironment environment)
    {
        string path = Path.Combine(Root, snapshot, "manifest.json");
        EvidenceManifest manifest = JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(path))!;
        File.WriteAllText(path, JsonSerializer.Serialize(manifest with { Environment = environment }, JsonOptions));
    }

    public void ReplaceCapturePath(string snapshot, string? pathValue)
    {
        string path = Path.Combine(Root, snapshot, "manifest.json");
        EvidenceManifest manifest = JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(path))!;
        EvidenceCapture capture = manifest.Captures.Single() with { Path = pathValue! };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest with { Captures = new[] { capture } }, JsonOptions));
    }

    public void ReplaceCaptureHash(string snapshot, string hash)
    {
        string path = Path.Combine(Root, snapshot, "manifest.json");
        EvidenceManifest manifest = JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(path))!;
        EvidenceCapture capture = manifest.Captures.Single() with { Sha256 = hash };
        File.WriteAllText(path, JsonSerializer.Serialize(manifest with { Captures = new[] { capture } }, JsonOptions));
    }

    public void ReplaceCaptureBytes(string snapshot, byte[] bytes)
    {
        string manifestPath = Path.Combine(Root, snapshot, "manifest.json");
        EvidenceManifest manifest = JsonSerializer.Deserialize<EvidenceManifest>(File.ReadAllText(manifestPath))!;
        EvidenceCapture existing = manifest.Captures.Single();
        File.WriteAllBytes(Path.Combine(Root, snapshot, existing.Path), bytes);
        EvidenceCapture capture = existing with
        {
            Sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
        };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest with { Captures = new[] { capture } }, JsonOptions));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private void WriteSnapshot(string snapshot, string revision, CaptureEnvironment environment, bool singleColor)
    {
        string directory = Path.Combine(Root, snapshot, "captures");
        Directory.CreateDirectory(directory);
        string imagePath = Path.Combine(directory, "home.png");
        byte[] png = CreatePng(singleColor);
        File.WriteAllBytes(imagePath, png);
        var manifest = new EvidenceManifest
        {
            SchemaVersion = 1,
            Snapshot = snapshot,
            Revision = revision,
            Environment = environment,
            Captures = new[]
            {
                new EvidenceCapture
                {
                    Key = "home-dark-small",
                    Label = "Home, dark theme, small window",
                    Path = "captures/home.png",
                    Width = 8,
                    Height = 8,
                    Sha256 = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(),
                },
            },
        };
        File.WriteAllText(
            Path.Combine(Root, snapshot, "manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
    }

    private static byte[] CreatePng(bool singleColor)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.White);
        if (!singleColor)
        {
            bitmap.SetPixel(4, 4, SKColors.Black);
        }
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
