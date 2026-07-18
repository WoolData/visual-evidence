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

    [Fact]
    public async Task BuildAsync_ProducesEvidenceItsPairValidatorAcceptsForLeadingUnderscoreAndUppercaseExtension()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-builder-tests", Guid.NewGuid().ToString("N"));
        CaptureEnvironment environment = EvidenceFixture.CreateEnvironment("fonts") with { CompatibilityKey = string.Empty };
        try
        {
            foreach ((string snapshot, char revision) in new[] { ("before", '1'), ("after", '2') })
            {
                string snapshotRoot = Path.Combine(root, snapshot);
                string captures = Path.Combine(snapshotRoot, "captures");
                Directory.CreateDirectory(captures);
                await File.WriteAllBytesAsync(
                    Path.Combine(captures, "_hidden.PNG"),
                    CreatePng(),
                    TestContext.Current.CancellationToken);
                EvidenceManifest manifest = await EvidenceManifestBuilder.BuildAsync(
                    snapshot,
                    new string(revision, 40),
                    captures,
                    Path.Combine(snapshotRoot, "manifest.json"),
                    environment,
                    TestContext.Current.CancellationToken);
                Assert.Equal("hidden", Assert.Single(manifest.Captures).Key);
            }

            ValidatedEvidencePair result = await new EvidencePairValidator().ValidateAsync(
                root,
                cancellationToken: TestContext.Current.CancellationToken);

            Assert.Single(result.Captures);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_EnforcesConfiguredCaptureCount()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-builder-tests", Guid.NewGuid().ToString("N"));
        string captures = Path.Combine(root, "after", "captures");
        Directory.CreateDirectory(captures);
        try
        {
            await File.WriteAllBytesAsync(Path.Combine(captures, "one.png"), CreatePng(), TestContext.Current.CancellationToken);
            await File.WriteAllBytesAsync(Path.Combine(captures, "two.png"), CreatePng(), TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<EvidenceValidationException>(() => EvidenceManifestBuilder.BuildAsync(
                "after",
                new string('2', 40),
                captures,
                Path.Combine(root, "after", "manifest.json"),
                EvidenceFixture.CreateEnvironment("fonts") with { CompatibilityKey = string.Empty },
                new EvidenceValidationOptions { MaximumCaptureCount = 1 },
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task BuildAsync_RejectsSingleColorCaptureLikeTheValidator()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-builder-tests", Guid.NewGuid().ToString("N"));
        string captures = Path.Combine(root, "after", "captures");
        Directory.CreateDirectory(captures);
        try
        {
            using var bitmap = new SKBitmap(4, 4);
            bitmap.Erase(SKColors.White);
            using SKImage image = SKImage.FromBitmap(bitmap);
            using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
            await File.WriteAllBytesAsync(
                Path.Combine(captures, "blank.png"),
                data.ToArray(),
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<EvidenceValidationException>(() => EvidenceManifestBuilder.BuildAsync(
                "after",
                new string('2', 40),
                captures,
                Path.Combine(root, "after", "manifest.json"),
                EvidenceFixture.CreateEnvironment("fonts") with { CompatibilityKey = string.Empty },
                TestContext.Current.CancellationToken));
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
