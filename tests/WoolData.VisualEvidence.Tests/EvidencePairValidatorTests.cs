// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.Tests;

using SkiaSharp;

public sealed class EvidencePairValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsMatchingExactRevisionEvidence()
    {
        using var fixture = new EvidenceFixture();
        var validator = new EvidencePairValidator();

        ValidatedEvidencePair result = await validator.ValidateAsync(
            fixture.Root,
            fixture.BeforeRevision,
            fixture.AfterRevision,
            TestContext.Current.CancellationToken);

        ValidatedImagePair pair = Assert.Single(result.Captures);
        Assert.Equal("home-dark-small", pair.Key);
        Assert.Equal(8, pair.After.Width);
        Assert.NotEmpty(pair.After.NormalizedPng);
    }

    [Fact]
    public async Task ValidateAsync_RejectsDifferentCaptureEnvironment()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceEnvironment("after", EvidenceFixture.CreateEnvironment("different-fonts"));

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("incompatible", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsManifestCompatibilityKeyThatDoesNotMatchFields()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceEnvironment("after", fixture.Environment with { CompatibilityKey = new string('0', 64) });

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("does not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsPathTraversal()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceCapturePath("after", "../../outside.png");

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("escapes", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsHashMismatch()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceCaptureHash("after", new string('a', 64));

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidateAsync_RejectsNullCapturePathCleanly()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceCapturePath("after", null!);

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("relative path", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsSingleColorFrame()
    {
        using var fixture = new EvidenceFixture(singleColor: true);

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("single-color", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsRenamedNonPngImage()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceCaptureBytes("after", CreateImage(SKEncodedImageFormat.Jpeg, transparent: false));

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => new EvidencePairValidator().ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("another image format", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateAsync_RejectsFullyTransparentFrameEvenWhenSingleColorAllowed()
    {
        using var fixture = new EvidenceFixture();
        fixture.ReplaceCaptureBytes("after", CreateImage(SKEncodedImageFormat.Png, transparent: true));
        var validator = new EvidencePairValidator(new EvidenceValidationOptions { RejectSingleColorImages = false });

        EvidenceValidationException error = await Assert.ThrowsAsync<EvidenceValidationException>(
            () => validator.ValidateAsync(
                fixture.Root,
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("fully transparent", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateImage(SKEncodedImageFormat format, bool transparent)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(transparent ? SKColors.Transparent : SKColors.White);
        if (!transparent)
        {
            bitmap.SetPixel(4, 4, SKColors.Black);
        }
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(format, 90);
        return data.ToArray();
    }
}
