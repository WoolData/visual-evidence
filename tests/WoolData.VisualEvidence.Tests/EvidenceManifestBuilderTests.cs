// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Security.Cryptography;
using SkiaSharp;

namespace WoolData.VisualEvidence.Tests;

public sealed class EvidenceManifestBuilderTests
{
    [Fact]
    public async Task BuildAsync_ProducesPortableRelativeManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-builder-tests", Guid.NewGuid().ToString("N"));
        string captures = Path.Combine(root, "after", "captures");
        string output = Path.Combine(root, "after", "manifest.json");
        Directory.CreateDirectory(captures);
        byte[] png = CreatePng();
        await File.WriteAllBytesAsync(
            Path.Combine(captures, "Home Dark.png"),
            png,
            TestContext.Current.CancellationToken);
        CaptureEnvironment environment = EvidenceFixture.CreateEnvironment("fonts") with { CompatibilityKey = string.Empty };
        try
        {
            EvidenceManifest manifest = await EvidenceManifestBuilder.BuildAsync(
                "after",
                new string('2', 40),
                captures,
                output,
                environment,
                TestContext.Current.CancellationToken);

            EvidenceCapture capture = Assert.Single(manifest.Captures);
            Assert.Equal("home-dark", capture.Key);
            Assert.Equal("captures/Home Dark.png", capture.Path);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant(), capture.Sha256);
            Assert.Equal(environment.CalculateCompatibilityKey(), manifest.Environment.CompatibilityKey);
            Assert.True(File.Exists(output));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static byte[] CreatePng()
    {
        using var bitmap = new SKBitmap(4, 4);
        bitmap.Erase(SKColors.White);
        bitmap.SetPixel(2, 2, SKColors.Black);
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
