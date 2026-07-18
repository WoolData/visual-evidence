// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using SkiaSharp;

namespace WoolData.VisualEvidence;

public static class AiReviewTransportImageFactory
{
    public const int DefaultMaximumEdge = 1568;

    public static IReadOnlyList<AiReviewTransportPair> CreateComparison(
        ValidatedEvidencePair evidence,
        int maximumEdge = DefaultMaximumEdge)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (maximumEdge is < 1 or > 8192)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEdge),
                "Transport maximum edge must be between 1 and 8192 pixels.");
        }

        return evidence.Captures
            .Select(pair => new AiReviewTransportPair(
                pair.Key,
                pair.Label,
                Create(pair.Before, maximumEdge),
                Create(pair.After, maximumEdge)))
            .ToArray();
    }

    public static AiReviewTransportImage Create(ValidatedImage image, int maximumEdge = DefaultMaximumEdge)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (maximumEdge is < 1 or > 8192)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumEdge),
                "Transport maximum edge must be between 1 and 8192 pixels.");
        }

        using SKData data = SKData.CreateCopy(image.NormalizedPng);
        using SKBitmap source = SKBitmap.Decode(data)
            ?? throw new EvidenceValidationException($"Validated capture '{image.Key}' could not be decoded for AI transport.");

        int longestEdge = Math.Max(source.Width, source.Height);
        if (longestEdge <= maximumEdge)
        {
            return new AiReviewTransportImage(
                image.SourceSha256,
                source.Width,
                source.Height,
                image.NormalizedPng.ToArray());
        }

        double scale = (double)maximumEdge / longestEdge;
        int width = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero));
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap resized = source.Resize(
            info,
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new EvidenceValidationException($"Validated capture '{image.Key}' could not be resized for AI transport.");
        using SKImage transport = SKImage.FromBitmap(resized);
        using SKData encoded = transport.Encode(SKEncodedImageFormat.Png, 100)
            ?? throw new EvidenceValidationException($"Validated capture '{image.Key}' could not be encoded for AI transport.");
        return new AiReviewTransportImage(image.SourceSha256, width, height, encoded.ToArray());
    }
}
