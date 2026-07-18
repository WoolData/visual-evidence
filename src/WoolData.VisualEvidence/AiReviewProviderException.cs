// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public sealed class AiReviewProviderException : Exception
{
    public AiReviewProviderException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
