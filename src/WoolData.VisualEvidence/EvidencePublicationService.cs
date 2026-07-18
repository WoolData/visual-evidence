// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public sealed class EvidencePublicationService
{
    private readonly string _repository;
    private readonly IChangeRequestProvider _changeRequests;
    private readonly IEvidenceAssetStore _assets;
    private readonly IAiReviewAssetStore? _aiReviewAssets;
    private readonly IImageAssetStore? _imageAssets;
    private readonly IReviewPublisher _reviews;
    private readonly IStatusPublisher? _statuses;
    private readonly EvidencePairValidator _validator;

    public EvidencePublicationService(
        string repository,
        IChangeRequestProvider changeRequests,
        IEvidenceAssetStore assets,
        IReviewPublisher reviews,
        IStatusPublisher? statuses = null,
        EvidencePairValidator? validator = null,
        IImageAssetStore? imageAssets = null)
    {
        _repository = repository;
        _changeRequests = changeRequests;
        _assets = assets;
        _aiReviewAssets = assets as IAiReviewAssetStore;
        _imageAssets = imageAssets ?? assets as IImageAssetStore;
        _reviews = reviews;
        _statuses = statuses;
        _validator = validator ?? new EvidencePairValidator();
    }

    public async Task<ImageAssetPublication> PublishImagesAsync(
        int changeNumber,
        IEnumerable<string> imagePaths,
        string summary,
        EvidenceImageSetValidator? validator = null,
        CancellationToken cancellationToken = default)
    {
        if (_imageAssets is null)
        {
            throw new InvalidOperationException("The configured asset store does not support image-set publication.");
        }
        ChangeRequestRevision revision = await _changeRequests.ResolveRevisionAsync(
            changeNumber,
            cancellationToken).ConfigureAwait(false);
        try
        {
            ValidatedImageSet evidence = await (validator ?? new EvidenceImageSetValidator()).ValidateAsync(
                imagePaths,
                cancellationToken).ConfigureAwait(false);
            ImageAssetPublication publication = await _imageAssets.PublishImagesAsync(
                changeNumber,
                revision.HeadRevision,
                evidence,
                cancellationToken).ConfigureAwait(false);
            string markdown = ReviewMarkdown.BuildImages(_repository, revision, publication, summary);
            await _reviews.PublishOrUpdateAsync(changeNumber, markdown, cancellationToken).ConfigureAwait(false);
            if (_statuses is not null)
            {
                await _statuses.PublishStatusAsync(
                    revision.HeadRevision,
                    "success",
                    "Current visual evidence is published.",
                    "visual-evidence/published",
                    cancellationToken).ConfigureAwait(false);
            }
            return publication;
        }
        catch
        {
            await TryPublishFailureStatusAsync(revision.HeadRevision, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<AssetPublication> PublishAsync(
        int changeNumber,
        string evidenceRoot,
        string summary,
        CancellationToken cancellationToken = default) =>
        await PublishComparisonAsync(
            changeNumber,
            evidenceRoot,
            summary,
            aiReview: null,
            cancellationToken).ConfigureAwait(false);

    public async Task<AssetPublication> PublishWithAiReviewAsync(
        int changeNumber,
        string evidenceRoot,
        string aiReviewPath,
        string summary,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(aiReviewPath);
        if (_aiReviewAssets is null)
        {
            throw new InvalidOperationException("The configured asset store does not support AI review publication.");
        }

        AiReviewDocument aiReview = await AiReviewDocumentCodec.ReadAsync(
            aiReviewPath,
            cancellationToken).ConfigureAwait(false);
        return await PublishComparisonAsync(
            changeNumber,
            evidenceRoot,
            summary,
            aiReview,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<AssetPublication> PublishComparisonAsync(
        int changeNumber,
        string evidenceRoot,
        string summary,
        AiReviewDocument? aiReview,
        CancellationToken cancellationToken)
    {
        ChangeRequestRevision revision = await _changeRequests.ResolveRevisionAsync(
            changeNumber,
            cancellationToken).ConfigureAwait(false);

        try
        {
            ValidatedEvidencePair evidence = await _validator.ValidateAsync(
                evidenceRoot,
                revision.MergeBaseRevision,
                revision.HeadRevision,
                cancellationToken).ConfigureAwait(false);
            if (aiReview is not null)
            {
                AiReviewProvenanceValidator.ValidateComparison(aiReview, evidence);
            }

            AssetPublication publication = aiReview is null
                ? await _assets.PublishAsync(
                    changeNumber,
                    revision.HeadRevision,
                    evidence,
                    cancellationToken).ConfigureAwait(false)
                : await _aiReviewAssets!.PublishWithAiReviewAsync(
                    changeNumber,
                    revision.HeadRevision,
                    evidence,
                    aiReview,
                    cancellationToken).ConfigureAwait(false);
            string markdown = ReviewMarkdown.Build(_repository, revision, publication, summary, aiReview);
            await _reviews.PublishOrUpdateAsync(changeNumber, markdown, cancellationToken).ConfigureAwait(false);
            if (_statuses is not null)
            {
                await _statuses.PublishStatusAsync(
                    revision.HeadRevision,
                    "success",
                    "Current visual evidence is published.",
                    "visual-evidence/published",
                    cancellationToken).ConfigureAwait(false);
            }
            return publication;
        }
        catch
        {
            await TryPublishFailureStatusAsync(revision.HeadRevision, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private async Task TryPublishFailureStatusAsync(string revision, CancellationToken cancellationToken)
    {
        if (_statuses is null)
        {
            return;
        }

        try
        {
            await _statuses.PublishStatusAsync(
                revision,
                "failure",
                "Visual evidence publication failed.",
                "visual-evidence/published",
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Failure status is best effort; the original publication exception is authoritative.
        }
    }

    public async Task VerifyAsync(int changeNumber, CancellationToken cancellationToken = default)
    {
        ChangeRequestRevision revision = await _changeRequests.ResolveRevisionAsync(
            changeNumber,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<string> comments = await _reviews.ReadCommentsAsync(changeNumber, cancellationToken).ConfigureAwait(false);
        string headMarker = $"Visual-Evidence-Head: {revision.HeadRevision}";
        string baseMarker = $"Visual-Evidence-Base: {revision.MergeBaseRevision}";
        bool current = comments.Any(comment => IsCurrentEvidenceComment(
            comment,
            changeNumber,
            revision.HeadRevision,
            headMarker,
            baseMarker));
        if (!current)
        {
            throw new EvidenceValidationException(
                $"Change request #{changeNumber} has no visual evidence for head {revision.HeadRevision} and merge base {revision.MergeBaseRevision}.");
        }
    }

    private bool IsCurrentEvidenceComment(
        string comment,
        int changeNumber,
        string headRevision,
        string headMarker,
        string baseMarker)
    {
        if (!comment.Contains(ReviewMarkdown.Marker, StringComparison.Ordinal) ||
            !comment.Contains(headMarker, StringComparison.Ordinal) ||
            !comment.Contains(baseMarker, StringComparison.Ordinal))
        {
            return false;
        }

        System.Text.RegularExpressions.Match commitMatch = System.Text.RegularExpressions.Regex.Match(
            comment,
            "(?m)^Visual-Evidence-Asset-Commit: (?<sha>[0-9a-f]{40}|[0-9a-f]{64})\r?$",
            System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (!commitMatch.Success)
        {
            return false;
        }

        string commit = commitMatch.Groups["sha"].Value;
        string beforePrefix = GitHubAssetUrl.BuildEvidencePrefix(
            _repository,
            commit,
            changeNumber,
            headRevision,
            "before");
        string afterPrefix = GitHubAssetUrl.BuildEvidencePrefix(
            _repository,
            commit,
            changeNumber,
            headRevision,
            "after");
        string imagePrefix = GitHubAssetUrl.BuildEvidencePrefix(
            _repository,
            commit,
            changeNumber,
            headRevision,
            "images");
        bool pair = comment.Contains($"]({beforePrefix}", StringComparison.Ordinal) &&
            comment.Contains($"]({afterPrefix}", StringComparison.Ordinal);
        bool imageSet = comment.Contains($"]({imagePrefix}", StringComparison.Ordinal);
        return pair || imageSet;
    }
}
