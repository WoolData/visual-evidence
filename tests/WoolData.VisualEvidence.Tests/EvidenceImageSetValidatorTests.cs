// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using SkiaSharp;

namespace WoolData.VisualEvidence.Tests;

public sealed class EvidenceImageSetValidatorTests
{
    [Fact]
    public async Task ValidateAsync_AcceptsIndividualPngAndDerivesAgentFriendlyMetadata()
    {
        string root = CreateRoot();
        try
        {
            string path = Path.Combine(root, "step-1-compact.png");
            WritePng(path, singleColor: false);

            ValidatedImageSet result = await new EvidenceImageSetValidator().ValidateAsync(
                [path],
                TestContext.Current.CancellationToken);

            ValidatedImage image = Assert.Single(result.Images);
            Assert.Equal("step-1-compact", image.Key);
            Assert.Equal("step 1 compact", image.Label);
            Assert.Equal(8, image.Width);
            Assert.NotEmpty(image.NormalizedPng);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ValidateDirectoryAsync_RejectsSingleColorCapture()
    {
        string root = CreateRoot();
        try
        {
            WritePng(Path.Combine(root, "blank.png"), singleColor: true);

            await Assert.ThrowsAsync<EvidenceValidationException>(() =>
                new EvidenceImageSetValidator().ValidateDirectoryAsync(root, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-image-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void WritePng(string path, bool singleColor)
    {
        using var bitmap = new SKBitmap(8, 8);
        bitmap.Erase(SKColors.White);
        if (!singleColor)
        {
            bitmap.SetPixel(4, 4, SKColors.Black);
        }
        using SKImage image = SKImage.FromBitmap(bitmap);
        using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(path, data.ToArray());
    }
}
