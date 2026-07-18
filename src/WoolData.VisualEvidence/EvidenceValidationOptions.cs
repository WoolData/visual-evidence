// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public sealed record EvidenceValidationOptions
{
    public long MaximumImageBytes { get; init; } = 10 * 1024 * 1024;

    public long MaximumPixels { get; init; } = 40_000_000;

    public int MaximumCaptureCount { get; init; } = 50;

    public bool RejectSingleColorImages { get; init; } = true;
}

public sealed class EvidenceValidationException : Exception
{
    public EvidenceValidationException(string message)
        : base(message)
    {
    }

    public EvidenceValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
