// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Net;
using System.Text;
using System.Text.Json;
using WoolData.VisualEvidence.GitHub;

namespace WoolData.VisualEvidence.Tests;

public sealed class GitHubApiClientTests
{
    [Fact]
    public async Task ResolveRevisionAsync_UsesPullAndCompareEndpoints()
    {
        string head = new('2', 40);
        string @base = new('3', 40);
        string mergeBase = new('1', 40);
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/repos/WoolData/example/pulls/17" => Json(
                HttpStatusCode.OK,
                JsonSerializer.Serialize(new { head = new { sha = head }, @base = new { sha = @base } })),
            var path when path.StartsWith("/repos/WoolData/example/compare/", StringComparison.Ordinal) =>
                Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { merge_base_commit = new { sha = mergeBase } })),
            _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"),
        });
        using var client = CreateClient(handler);

        ChangeRequestRevision revision = await client.ResolveRevisionAsync(17, TestContext.Current.CancellationToken);

        Assert.Equal(head, revision.HeadRevision);
        Assert.Equal(mergeBase, revision.MergeBaseRevision);
        Assert.Contains(handler.Requests, request => request.Contains($"compare/{@base}...{head}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PublishAsync_CreatesAppendOnlyAssetCommit()
    {
        int blob = 0;
        string? treeBody = null;
        string? refBody = null;
        var handler = new RecordingHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Post && path.EndsWith("/git/blobs", StringComparison.Ordinal))
            {
                blob++;
                return Json(HttpStatusCode.Created, $"{{\"sha\":\"{new string(blob == 1 ? 'a' : 'b', 40)}\"}}");
            }
            if (request.Method == HttpMethod.Get && path.Contains("/git/ref/heads/", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.NotFound, "{\"message\":\"Not Found\"}");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/git/trees", StringComparison.Ordinal))
            {
                treeBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.Created, $"{{\"sha\":\"{new string('c', 40)}\"}}");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/git/commits", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.Created, $"{{\"sha\":\"{new string('d', 40)}\"}}");
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/git/refs", StringComparison.Ordinal))
            {
                refBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
                return Json(HttpStatusCode.Created, "{}");
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        using var client = CreateClient(handler);
        string head = new('2', 40);
        ValidatedEvidencePair evidence = CreateValidatedEvidence();

        AssetPublication publication = await client.PublishAsync(17, head, evidence, TestContext.Current.CancellationToken);

        Assert.Equal(new string('d', 40), publication.CommitSha);
        Assert.Contains($"pr-17/{head}/before/home.png", treeBody, StringComparison.Ordinal);
        Assert.Contains($"pr-17/{head}/after/home.png", treeBody, StringComparison.Ordinal);
        Assert.Contains("refs/heads/visual-evidence-assets", refBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PublishOrUpdateAsync_UpdatesOwnMarkerComment()
    {
        HttpMethod? finalMethod = null;
        string? finalPath = null;
        var handler = new RecordingHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/user")
            {
                return Json(HttpStatusCode.OK, "{\"login\":\"visual-evidence-bot\"}");
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues/17/comments", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, $"[{{\"id\":42,\"body\":\"{ReviewMarkdown.Marker}\",\"user\":{{\"login\":\"visual-evidence-bot\"}}}}]");
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/issues/comments/42", StringComparison.Ordinal))
            {
                finalMethod = request.Method;
                finalPath = path;
                return Json(HttpStatusCode.OK, "{}");
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        using var client = CreateClient(handler);

        await client.PublishOrUpdateAsync(17, $"{ReviewMarkdown.Marker}\nnew", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Patch, finalMethod);
        Assert.Equal("/repos/WoolData/example/issues/comments/42", finalPath);
    }

    [Fact]
    public async Task PublishOrUpdateAsync_UsesGitHubActionsBotWhenUserEndpointRejectsInstallationToken()
    {
        HttpMethod? finalMethod = null;
        var handler = new RecordingHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path == "/user")
            {
                return Json(HttpStatusCode.Forbidden, "{\"message\":\"Resource not accessible by integration\"}");
            }
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues/17/comments", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    $"[{{\"id\":42,\"body\":\"{ReviewMarkdown.Marker}\",\"user\":{{\"login\":\"github-actions[bot]\"}}}}]");
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/issues/comments/42", StringComparison.Ordinal))
            {
                finalMethod = request.Method;
                return Json(HttpStatusCode.OK, "{}");
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        using var client = CreateClient(handler);

        await client.PublishOrUpdateAsync(17, $"{ReviewMarkdown.Marker}\nnew", TestContext.Current.CancellationToken);

        Assert.Equal(HttpMethod.Patch, finalMethod);
    }

    [Fact]
    public async Task ReadCommentsAsync_UsesGitHubActionsBotWhenUserEndpointRejectsInstallationToken()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/user" => Json(HttpStatusCode.Forbidden, "{\"message\":\"Resource not accessible by integration\"}"),
            "/repos/WoolData/example/issues/17/comments" => Json(
                HttpStatusCode.OK,
                $"[{{\"id\":42,\"body\":\"{ReviewMarkdown.Marker}\",\"user\":{{\"login\":\"github-actions[bot]\"}}}}]"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
        });
        using var client = CreateClient(handler);

        IReadOnlyList<string> comments = await client.ReadCommentsAsync(17, TestContext.Current.CancellationToken);

        Assert.Single(comments);
        Assert.Contains(ReviewMarkdown.Marker, comments[0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadCommentsAsync_DoesNotHideOtherAuthenticationFailures()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/user" => Json(HttpStatusCode.Unauthorized, "{\"message\":\"Bad credentials\"}"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
        });
        using var client = CreateClient(handler);

        GitHubApiException exception = await Assert.ThrowsAsync<GitHubApiException>(() =>
            client.ReadCommentsAsync(17, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Unauthorized, exception.StatusCode);
    }

    [Fact]
    public async Task ReadCommentsAsync_DoesNotHideUnrelatedForbiddenResponse()
    {
        var handler = new RecordingHandler(request => request.RequestUri!.AbsolutePath switch
        {
            "/user" => Json(HttpStatusCode.Forbidden, "{\"message\":\"Forbidden\"}"),
            _ => throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}"),
        });
        using var client = CreateClient(handler);

        GitHubApiException exception = await Assert.ThrowsAsync<GitHubApiException>(() =>
            client.ReadCommentsAsync(17, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
    }

    [Fact]
    public async Task ResolveRevisionAsync_WrapsTransportFailure()
    {
        var handler = new RecordingHandler(_ => throw new HttpRequestException("connection details"));
        using var client = CreateClient(handler);

        GitHubApiException exception = await Assert.ThrowsAsync<GitHubApiException>(() =>
            client.ResolveRevisionAsync(17, TestContext.Current.CancellationToken));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Equal("GitHub API request could not be completed.", exception.Message);
        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task PublishOrUpdateAsync_UsesConfiguredCustomAppLoginWithoutUserLookup()
    {
        var handler = new RecordingHandler(request =>
        {
            string path = request.RequestUri!.AbsolutePath;
            if (request.Method == HttpMethod.Get && path.EndsWith("/issues/17/comments", StringComparison.Ordinal))
            {
                return Json(
                    HttpStatusCode.OK,
                    $"[{{\"id\":42,\"body\":\"{ReviewMarkdown.Marker}\",\"user\":{{\"login\":\"custom-app[bot]\"}}}}]");
            }
            if (request.Method == HttpMethod.Patch && path.EndsWith("/issues/comments/42", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, "{}");
            }
            throw new InvalidOperationException($"Unexpected request: {request.Method} {request.RequestUri}");
        });
        using var client = CreateClient(handler, "custom-app[bot]");

        await client.PublishOrUpdateAsync(17, $"{ReviewMarkdown.Marker}\nnew", TestContext.Current.CancellationToken);

        Assert.DoesNotContain(handler.Requests, request => request.EndsWith(" /user", StringComparison.Ordinal));
    }

    private static GitHubApiClient CreateClient(HttpMessageHandler handler, string? commentAuthorLogin = null)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.github.test/") };
        return new GitHubApiClient(new GitHubOptions
        {
            Repository = "WoolData/example",
            Token = "test-token",
            CommentAuthorLogin = commentAuthorLogin,
        }, httpClient);
    }

    private static ValidatedEvidencePair CreateValidatedEvidence()
    {
        CaptureEnvironment environment = EvidenceFixture.CreateEnvironment("fonts");
        EvidenceManifest Manifest(string snapshot, char revision) => new()
        {
            SchemaVersion = 1,
            Snapshot = snapshot,
            Revision = new string(revision, 40),
            Environment = environment,
            Captures = Array.Empty<EvidenceCapture>(),
        };
        var before = new ValidatedImage("home", "Home", "before.png", 2, 2, new string('a', 64), new byte[] { 1, 2 });
        var after = new ValidatedImage("home", "Home", "after.png", 2, 2, new string('b', 64), new byte[] { 3, 4 });
        return new ValidatedEvidencePair(
            Manifest("before", '1'),
            Manifest("after", '2'),
            new[] { new ValidatedImagePair("home", "Home", before, after) });
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public List<string> Requests { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add($"{request.Method} {request.RequestUri}");
            return Task.FromResult(_responder(request));
        }
    }
}
