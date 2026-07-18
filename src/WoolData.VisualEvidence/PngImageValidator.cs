// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Security.Cryptography;
using SkiaSharp;

namespace WoolData.VisualEvidence;

internal static class PngImageValidator
{
    public static ValidatedImage Validate(
        string path,
        string key,
        string label,
        EvidenceValidationOptions options,
        int? expectedWidth = null,
        int? expectedHeight = null,
        string? expectedSha256 = null)
    {
        var file = new FileInfo(path);
        if (!file.Exists)
        {
            throw new EvidenceValidationException($"Capture '{key}' is missing: {path}");
        }
        if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new EvidenceValidationException($"Capture '{key}' cannot be a symbolic link or reparse point.");
        }
        DirectoryInfo? parent = file.Directory;
        if (parent is not null &&
            (parent.LinkTarget is not null || parent.Attributes.HasFlag(FileAttributes.ReparsePoint)))
        {
            throw new EvidenceValidationException($"Capture '{key}' cannot traverse a symbolic link or reparse point.");
        }
        if (!string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new EvidenceValidationException($"Capture '{key}' must be a PNG file.");
        }
        if (file.Length is <= 0 || file.Length > options.MaximumImageBytes)
        {
            throw new EvidenceValidationException($"Capture '{key}' exceeds the configured file-size boundary.");
        }

        byte[] sourceBytes = File.ReadAllBytes(path);
        string hash = Convert.ToHexString(SHA256.HashData(sourceBytes)).ToLowerInvariant();
        if (expectedSha256 is not null && !NormalizedHashEquals(hash, expectedSha256))
        {
            throw new EvidenceValidationException($"Capture '{key}' SHA-256 does not match its manifest.");
        }

        try
        {
            using var data = SKData.CreateCopy(sourceBytes);
            using SKCodec? codec = SKCodec.Create(data);
            if (codec is null)
            {
                throw new EvidenceValidationException($"Capture '{key}' is not a decodable PNG.");
            }
            if (codec.EncodedFormat != SKEncodedImageFormat.Png)
            {
                throw new EvidenceValidationException($"Capture '{key}' has a PNG extension but contains another image format.");
            }
            SKImageInfo info = codec.Info;
            long pixels = checked((long)info.Width * info.Height);
            if (info.Width <= 0 || info.Height <= 0 || pixels > options.MaximumPixels)
            {
                throw new EvidenceValidationException($"Capture '{key}' exceeds the configured pixel boundary.");
            }
            if ((expectedWidth is not null && info.Width != expectedWidth) ||
                (expectedHeight is not null && info.Height != expectedHeight))
            {
                throw new EvidenceValidationException(
                    $"Capture '{key}' dimensions are {info.Width}x{info.Height}; manifest declares {expectedWidth}x{expectedHeight}.");
            }

            var decodeInfo = new SKImageInfo(
                info.Width,
                info.Height,
                SKColorType.Rgba8888,
                SKAlphaType.Unpremul,
                info.ColorSpace);
            using SKBitmap? bitmap = SKBitmap.Decode(data, decodeInfo);
            if (bitmap is null)
            {
                throw new EvidenceValidationException($"Capture '{key}' could not be decoded.");
            }
            (bool isSingleColor, bool hasVisiblePixel) = InspectPixels(bitmap);
            if (!hasVisiblePixel)
            {
                throw new EvidenceValidationException($"Capture '{key}' is fully transparent.");
            }
            if (options.RejectSingleColorImages && isSingleColor)
            {
                throw new EvidenceValidationException($"Capture '{key}' is a single-color frame.");
            }

            using SKImage normalized = SKImage.FromBitmap(bitmap);
            using SKData normalizedData = normalized.Encode(SKEncodedImageFormat.Png, 100)
                ?? throw new EvidenceValidationException($"Capture '{key}' could not be normalized.");
            return new ValidatedImage(key, label, path, info.Width, info.Height, hash, normalizedData.ToArray());
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException or OverflowException or ArgumentException)
        {
            throw new EvidenceValidationException($"Capture '{key}' failed PNG validation.", ex);
        }
    }

    private static (bool IsSingleColor, bool HasVisiblePixel) InspectPixels(SKBitmap bitmap)
    {
        const int bytesPerPixel = 4;
        ReadOnlySpan<byte> pixels = bitmap.GetPixelSpan();
        ReadOnlySpan<byte> first = pixels[..bytesPerPixel];
        bool isSingleColor = true;
        bool hasVisiblePixel = false;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<byte> row = pixels.Slice(y * bitmap.RowBytes, bitmap.Width * bytesPerPixel);
            for (int offset = 0; offset < row.Length; offset += bytesPerPixel)
            {
                ReadOnlySpan<byte> pixel = row.Slice(offset, bytesPerPixel);
                hasVisiblePixel |= pixel[3] != 0;
                isSingleColor &= pixel.SequenceEqual(first);
            }
        }
        return (isSingleColor, hasVisiblePixel);
    }

    private static bool NormalizedHashEquals(string left, string right) =>
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
