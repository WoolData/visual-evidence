// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public static class AiReviewProvenanceValidator
{
    public static void ValidateComparison(
        AiReviewDocument review,
        ValidatedEvidencePair evidence)
    {
        ArgumentNullException.ThrowIfNull(review);
        ArgumentNullException.ThrowIfNull(evidence);
        AiReviewDocumentCodec.Validate(review);

        if (review.Task != "compare")
        {
            throw new EvidenceValidationException("AI review task must be compare for paired evidence.");
        }
        if (review.Reviews.Count != evidence.Captures.Count)
        {
            throw new EvidenceValidationException(
                "AI review entries must exactly match the validated capture set.");
        }

        IReadOnlyDictionary<string, AiReviewEntry> reviews = review.Reviews.ToDictionary(
            static entry => entry.Key,
            StringComparer.Ordinal);
        foreach (ValidatedImagePair pair in evidence.Captures)
        {
            if (!reviews.TryGetValue(pair.Key, out AiReviewEntry? entry))
            {
                throw new EvidenceValidationException(
                    $"AI review is missing validated capture '{pair.Key}'.");
            }
            if (entry.Source.ImageSha256 is not null ||
                !HashEquals(entry.Source.BeforeSha256, pair.Before.SourceSha256) ||
                !HashEquals(entry.Source.AfterSha256, pair.After.SourceSha256))
            {
                throw new EvidenceValidationException(
                    $"AI review source hashes do not match validated capture '{pair.Key}'.");
            }
        }
    }

    private static bool HashEquals(string? left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
