// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Security.Cryptography;
using System.Text;

namespace WoolData.VisualEvidence;

public static class AiReviewPrompt
{
    public static string CalculateSha256(string prompt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prompt))).ToLowerInvariant();
    }
}
