// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public sealed class EvidencePublicationService
{
    private readonly string _repository;
    private readonly IChangeRequestProvider _changeRequests;
    private readonly IEvidenceAssetStore _assets;
    private readonly IReviewPublisher _reviews;
    private readonly IStatusPublisher? _statuses;
    private readonly EvidencePairValidator _validator;

    public EvidencePublicationService(
        string repository,
        IChangeRequestProvider changeRequests,
        IEvidenceAssetStore assets,
        IReviewPublisher reviews,
        IStatusPublisher? statuses = null,
        EvidencePairValidator? validator = null)
    {
        _repository = repository;
        _changeRequests = changeRequests;
        _assets = assets;
        _reviews = reviews;
        _statuses = statuses;
        _validator = validator ?? new EvidencePairValidator();
    }

    public async Task<AssetPublication> PublishAsync(
        int changeNumber,
        string evidenceRoot,
        string summary,
        CancellationToken cancellationToken = default)
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
            AssetPublication publication = await _assets.PublishAsync(
                changeNumber,
                revision.HeadRevision,
                evidence,
                cancellationToken).ConfigureAwait(false);
            string markdown = ReviewMarkdown.Build(_repository, revision, publication, summary);
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
            if (_statuses is not null)
            {
                await _statuses.PublishStatusAsync(
                    revision.HeadRevision,
                    "failure",
                    "Visual evidence publication failed.",
                    "visual-evidence/published",
                    cancellationToken).ConfigureAwait(false);
            }
            throw;
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
        return comment.Contains($"]({beforePrefix}", StringComparison.Ordinal) &&
            comment.Contains($"]({afterPrefix}", StringComparison.Ordinal);
    }
}
