// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.Tests;

public sealed class EvidencePublicationServiceTests
{
    [Fact]
    public async Task VerifyAsync_RequiresExactHeadAndMergeBaseMarkers()
    {
        string head = new('2', 40);
        string mergeBase = new('1', 40);
        string assetCommit = new('5', 40);
        var provider = new FakeProvider(
            new ChangeRequestRevision(9, head, new string('3', 40), mergeBase),
            new[] { BuildComment(head, mergeBase, assetCommit) });
        var service = new EvidencePublicationService("WoolData/example", provider, provider, provider);

        await service.VerifyAsync(9, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task VerifyAsync_AcceptsCarriageReturnLineFeedComments()
    {
        string head = new('2', 40);
        string mergeBase = new('1', 40);
        string assetCommit = new('5', 40);
        string crlfComment = BuildComment(head, mergeBase, assetCommit).Replace("\n", "\r\n", StringComparison.Ordinal);
        var provider = new FakeProvider(
            new ChangeRequestRevision(9, head, new string('3', 40), mergeBase),
            new[] { crlfComment });
        var service = new EvidencePublicationService("WoolData/example", provider, provider, provider);

        await service.VerifyAsync(9, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task VerifyAsync_RejectsStaleHead()
    {
        string head = new('2', 40);
        string mergeBase = new('1', 40);
        var provider = new FakeProvider(
            new ChangeRequestRevision(9, head, new string('3', 40), mergeBase),
            new[] { BuildComment(new string('4', 40), mergeBase, new string('5', 40)) });
        var service = new EvidencePublicationService("WoolData/example", provider, provider, provider);

        await Assert.ThrowsAsync<EvidenceValidationException>(
            () => service.VerifyAsync(9, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PublishAsync_PreservesPublicationFailureWhenFailureStatusAlsoFails()
    {
        using var fixture = new EvidenceFixture();
        var publicationFailure = new InvalidOperationException("asset publication failed");
        var provider = new FakeProvider(
            new ChangeRequestRevision(9, fixture.AfterRevision, new string('3', 40), fixture.BeforeRevision),
            Array.Empty<string>(),
            publicationFailure,
            new InvalidOperationException("status publication failed"));
        var service = new EvidencePublicationService("WoolData/example", provider, provider, provider, provider);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.PublishAsync(9, fixture.Root, "summary", TestContext.Current.CancellationToken));

        Assert.Same(publicationFailure, exception);
    }

    private static string BuildComment(string head, string mergeBase, string assetCommit) => $$"""
        {{ReviewMarkdown.Marker}}
        Visual-Evidence-Head: {{head}}
        Visual-Evidence-Base: {{mergeBase}}
        Visual-Evidence-Asset-Commit: {{assetCommit}}
        ![Before: x](https://github.com/WoolData/example/blob/{{assetCommit}}/pr-9/{{head}}/before/x.png?raw=true)
        ![After: x](https://github.com/WoolData/example/blob/{{assetCommit}}/pr-9/{{head}}/after/x.png?raw=true)
        """;

    private sealed class FakeProvider : IChangeRequestProvider, IEvidenceAssetStore, IReviewPublisher, IStatusPublisher
    {
        private readonly ChangeRequestRevision _revision;
        private readonly IReadOnlyList<string> _comments;
        private readonly Exception? _publicationException;
        private readonly Exception? _statusException;

        public FakeProvider(
            ChangeRequestRevision revision,
            IReadOnlyList<string> comments,
            Exception? publicationException = null,
            Exception? statusException = null)
        {
            _revision = revision;
            _comments = comments;
            _publicationException = publicationException;
            _statusException = statusException;
        }

        public Task<ChangeRequestRevision> ResolveRevisionAsync(int number, CancellationToken cancellationToken = default) =>
            Task.FromResult(_revision);

        public Task<AssetPublication> PublishAsync(int changeNumber, string headRevision, ValidatedEvidencePair evidence, CancellationToken cancellationToken = default) =>
            Task.FromException<AssetPublication>(_publicationException ?? new NotSupportedException());

        public Task PublishOrUpdateAsync(int changeNumber, string markdown, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ReadCommentsAsync(int changeNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult(_comments);

        public Task PublishStatusAsync(string revision, string state, string description, string context, CancellationToken cancellationToken = default) =>
            _statusException is null ? Task.CompletedTask : Task.FromException(_statusException);
    }
}
