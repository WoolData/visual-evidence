// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public interface IChangeRequestProvider
{
    Task<ChangeRequestRevision> ResolveRevisionAsync(int number, CancellationToken cancellationToken = default);
}

public interface IEvidenceAssetStore
{
    Task<AssetPublication> PublishAsync(
        int changeNumber,
        string headRevision,
        ValidatedEvidencePair evidence,
        CancellationToken cancellationToken = default);
}

public interface IReviewPublisher
{
    Task PublishOrUpdateAsync(
        int changeNumber,
        string markdown,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadCommentsAsync(
        int changeNumber,
        CancellationToken cancellationToken = default);
}

public interface IStatusPublisher
{
    Task PublishStatusAsync(
        string revision,
        string state,
        string description,
        string context,
        CancellationToken cancellationToken = default);
}
