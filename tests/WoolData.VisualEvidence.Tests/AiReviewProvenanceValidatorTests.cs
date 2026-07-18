// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.Tests;

public sealed class AiReviewProvenanceValidatorTests
{
    [Fact]
    public async Task ValidateComparison_AcceptsExactValidatedSourceHashes()
    {
        using var fixture = new EvidenceFixture();
        ValidatedEvidencePair evidence = await new EvidencePairValidator().ValidateAsync(
            fixture.Root,
            cancellationToken: TestContext.Current.CancellationToken);
        AiReviewDocument review = CreateReview(evidence);

        AiReviewProvenanceValidator.ValidateComparison(review, evidence);
    }

    [Fact]
    public async Task ValidateComparison_RejectsReviewBoundToDifferentPixels()
    {
        using var fixture = new EvidenceFixture();
        ValidatedEvidencePair evidence = await new EvidencePairValidator().ValidateAsync(
            fixture.Root,
            cancellationToken: TestContext.Current.CancellationToken);
        AiReviewDocument review = CreateReview(evidence);
        AiReviewEntry entry = review.Reviews.Single();
        review = review with
        {
            Reviews = new[]
            {
                entry with
                {
                    Source = entry.Source with { AfterSha256 = new string('f', 64) },
                },
            },
        };

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewProvenanceValidator.ValidateComparison(review, evidence));

        Assert.Contains("do not match", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateComparison_RejectsMissingCaptureReview()
    {
        using var fixture = new EvidenceFixture();
        ValidatedEvidencePair evidence = await new EvidencePairValidator().ValidateAsync(
            fixture.Root,
            cancellationToken: TestContext.Current.CancellationToken);
        AiReviewDocument review = CreateReview(evidence) with { Reviews = Array.Empty<AiReviewEntry>() };

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewProvenanceValidator.ValidateComparison(review, evidence));

        Assert.Contains("between 1", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static AiReviewDocument CreateReview(ValidatedEvidencePair evidence)
    {
        ValidatedImagePair pair = evidence.Captures.Single();
        return AiReviewDocumentCodecTests.CreateDocument() with
        {
            Reviews = new[]
            {
                AiReviewDocumentCodecTests.CreateDocument().Reviews.Single() with
                {
                    Key = pair.Key,
                    Source = new AiReviewSource
                    {
                        BeforeSha256 = pair.Before.SourceSha256,
                        AfterSha256 = pair.After.SourceSha256,
                    },
                },
            },
        };
    }
}
