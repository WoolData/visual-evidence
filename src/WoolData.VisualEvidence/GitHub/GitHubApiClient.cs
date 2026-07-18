// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WoolData.VisualEvidence.GitHub;

public sealed class GitHubApiClient :
    IChangeRequestProvider,
    IEvidenceAssetStore,
    IAiReviewAssetStore,
    IImageAssetStore,
    IReviewPublisher,
    IStatusPublisher,
    IDisposable
{
    private const string ApiVersion = "2022-11-28";
    private readonly GitHubOptions _options;
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private string? _login;

    public GitHubApiClient(GitHubOptions options, HttpClient? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                options.Repository,
                "^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Repository must use owner/name form.", nameof(options));
        }
        if (!System.Text.RegularExpressions.Regex.IsMatch(
                options.AssetsBranch,
                "^[A-Za-z0-9._/-]+$",
                System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            options.AssetsBranch.StartsWith("/", StringComparison.Ordinal) ||
            options.AssetsBranch.EndsWith("/", StringComparison.Ordinal) ||
            options.AssetsBranch.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Assets branch contains unsupported syntax.", nameof(options));
        }

        _options = options;
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.BaseAddress ??= new Uri(options.ApiUrl.TrimEnd('/') + "/", UriKind.Absolute);
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.Token);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("WoolData-VisualEvidence/0.1");
        _httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", ApiVersion);
    }

    public async Task<ChangeRequestRevision> ResolveRevisionAsync(
        int number,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument pull = await SendJsonAsync(
            HttpMethod.Get,
            $"repos/{_options.Repository}/pulls/{number}",
            null,
            cancellationToken).ConfigureAwait(false);
        string head = RequiredString(pull.RootElement.GetProperty("head"), "sha");
        string @base = RequiredString(pull.RootElement.GetProperty("base"), "sha");
        using JsonDocument comparison = await SendJsonAsync(
            HttpMethod.Get,
            $"repos/{_options.Repository}/compare/{@base}...{head}",
            null,
            cancellationToken).ConfigureAwait(false);
        string mergeBase = RequiredString(comparison.RootElement.GetProperty("merge_base_commit"), "sha");
        return new ChangeRequestRevision(number, head, @base, mergeBase);
    }

    public async Task<AssetPublication> PublishAsync(
        int changeNumber,
        string headRevision,
        ValidatedEvidencePair evidence,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<PendingAsset>(evidence.Captures.Count * 2);
        foreach (ValidatedImagePair pair in evidence.Captures)
        {
            string beforePath = $"pr-{changeNumber}/{headRevision}/before/{pair.Key}.png";
            string afterPath = $"pr-{changeNumber}/{headRevision}/after/{pair.Key}.png";
            string beforeBlob = await CreateBlobAsync(pair.Before.NormalizedPng, cancellationToken).ConfigureAwait(false);
            string afterBlob = await CreateBlobAsync(pair.After.NormalizedPng, cancellationToken).ConfigureAwait(false);
            entries.Add(new PendingAsset(pair.Key, pair.Label, beforePath, beforeBlob, true));
            entries.Add(new PendingAsset(pair.Key, pair.Label, afterPath, afterBlob, false));
        }

        string commitSha = await PublishEntriesAsync(
            changeNumber,
            headRevision,
            entries,
            cancellationToken).ConfigureAwait(false);

        IReadOnlyList<PublishedAsset> assets = evidence.Captures.Select(pair => new PublishedAsset(
            pair.Key,
            pair.Label,
            $"pr-{changeNumber}/{headRevision}/before/{pair.Key}.png",
            $"pr-{changeNumber}/{headRevision}/after/{pair.Key}.png")).ToArray();
        return new AssetPublication(commitSha, assets);
    }

    public async Task<AssetPublication> PublishWithAiReviewAsync(
        int changeNumber,
        string headRevision,
        ValidatedEvidencePair evidence,
        AiReviewDocument review,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(review);
        AiReviewProvenanceValidator.ValidateComparison(review, evidence);
        byte[] reviewBytes = AiReviewDocumentCodec.Serialize(review);

        var entries = new List<PendingAsset>((evidence.Captures.Count * 2) + 1);
        foreach (ValidatedImagePair pair in evidence.Captures)
        {
            string beforePath = $"pr-{changeNumber}/{headRevision}/before/{pair.Key}.png";
            string afterPath = $"pr-{changeNumber}/{headRevision}/after/{pair.Key}.png";
            string beforeBlob = await CreateBlobAsync(pair.Before.NormalizedPng, cancellationToken).ConfigureAwait(false);
            string afterBlob = await CreateBlobAsync(pair.After.NormalizedPng, cancellationToken).ConfigureAwait(false);
            entries.Add(new PendingAsset(pair.Key, pair.Label, beforePath, beforeBlob, true));
            entries.Add(new PendingAsset(pair.Key, pair.Label, afterPath, afterBlob, false));
        }

        string reviewPath = $"pr-{changeNumber}/{headRevision}/ai-review-v1.json";
        string reviewBlob = await CreateBlobAsync(reviewBytes, cancellationToken).ConfigureAwait(false);
        entries.Add(new PendingAsset("ai-review-v1", "Advisory AI review", reviewPath, reviewBlob, true));
        string commitSha = await PublishEntriesAsync(
            changeNumber,
            headRevision,
            entries,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PublishedAsset> assets = evidence.Captures.Select(pair => new PublishedAsset(
            pair.Key,
            pair.Label,
            $"pr-{changeNumber}/{headRevision}/before/{pair.Key}.png",
            $"pr-{changeNumber}/{headRevision}/after/{pair.Key}.png")).ToArray();
        return new AssetPublication(commitSha, assets, reviewPath);
    }

    public async Task<ImageAssetPublication> PublishImagesAsync(
        int changeNumber,
        string headRevision,
        ValidatedImageSet evidence,
        CancellationToken cancellationToken = default)
    {
        var entries = new List<PendingAsset>(evidence.Images.Count);
        foreach (ValidatedImage image in evidence.Images)
        {
            string path = $"pr-{changeNumber}/{headRevision}/images/{image.Key}.png";
            string blob = await CreateBlobAsync(image.NormalizedPng, cancellationToken).ConfigureAwait(false);
            entries.Add(new PendingAsset(image.Key, image.Label, path, blob, true));
        }

        string commitSha = await PublishEntriesAsync(
            changeNumber,
            headRevision,
            entries,
            cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PublishedImageAsset> assets = evidence.Images.Select(image => new PublishedImageAsset(
            image.Key,
            image.Label,
            $"pr-{changeNumber}/{headRevision}/images/{image.Key}.png")).ToArray();
        return new ImageAssetPublication(commitSha, assets);
    }

    private async Task<string> PublishEntriesAsync(
        int changeNumber,
        string headRevision,
        IReadOnlyList<PendingAsset> entries,
        CancellationToken cancellationToken)
    {
        string? commitSha = null;
        for (int attempt = 1; attempt <= _options.MaximumPublishAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GitReference? reference = await TryGetReferenceAsync(cancellationToken).ConfigureAwait(false);
            string? parentSha = reference?.Sha;
            string? baseTreeSha = parentSha is null
                ? null
                : await GetCommitTreeAsync(parentSha, cancellationToken).ConfigureAwait(false);
            string treeSha = await CreateTreeAsync(entries, baseTreeSha, cancellationToken).ConfigureAwait(false);
            if (parentSha is not null && string.Equals(treeSha, baseTreeSha, StringComparison.OrdinalIgnoreCase))
            {
                return parentSha;
            }
            string candidate = await CreateCommitAsync(
                $"Add visual evidence for change #{changeNumber} at {headRevision}",
                treeSha,
                parentSha,
                cancellationToken).ConfigureAwait(false);
            try
            {
                await UpdateReferenceAsync(candidate, parentSha is null, cancellationToken).ConfigureAwait(false);
                commitSha = candidate;
                break;
            }
            catch (GitHubApiException ex) when (attempt < _options.MaximumPublishAttempts && ex.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(150 * attempt), cancellationToken).ConfigureAwait(false);
            }
        }

        return commitSha ?? throw new GitHubApiException(
            HttpStatusCode.Conflict,
            "Could not update the evidence branch after concurrent publication retries.");
    }

    public async Task PublishOrUpdateAsync(
        int changeNumber,
        string markdown,
        CancellationToken cancellationToken = default)
    {
        string login = await GetLoginAsync(cancellationToken).ConfigureAwait(false);
        ExistingComment? comment = (await ReadCommentRecordsAsync(changeNumber, cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(item =>
                string.Equals(item.Login, login, StringComparison.OrdinalIgnoreCase) &&
                item.Body.Contains(ReviewMarkdown.Marker, StringComparison.Ordinal));
        if (comment is null)
        {
            using JsonDocument _ = await SendJsonAsync(
                HttpMethod.Post,
                $"repos/{_options.Repository}/issues/{changeNumber}/comments",
                Serialize(new GitHubCommentPayload(markdown), VisualEvidenceJsonContext.Default.GitHubCommentPayload),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        using JsonDocument updated = await SendJsonAsync(
            HttpMethod.Patch,
            $"repos/{_options.Repository}/issues/comments/{comment.Id}",
            Serialize(new GitHubCommentPayload(markdown), VisualEvidenceJsonContext.Default.GitHubCommentPayload),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ReadCommentsAsync(
        int changeNumber,
        CancellationToken cancellationToken = default)
    {
        string login = await GetLoginAsync(cancellationToken).ConfigureAwait(false);
        return (await ReadCommentRecordsAsync(changeNumber, cancellationToken).ConfigureAwait(false))
            .Where(comment => string.Equals(comment.Login, login, StringComparison.OrdinalIgnoreCase))
            .Select(comment => comment.Body)
            .ToArray();
    }

    public async Task PublishStatusAsync(
        string revision,
        string state,
        string description,
        string context,
        CancellationToken cancellationToken = default)
    {
        using JsonDocument _ = await SendJsonAsync(
            HttpMethod.Post,
            $"repos/{_options.Repository}/statuses/{revision}",
            Serialize(new GitHubStatusPayload(state, description, context), VisualEvidenceJsonContext.Default.GitHubStatusPayload),
            cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private async Task<string> CreateBlobAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        using JsonDocument result = await SendJsonAsync(
            HttpMethod.Post,
            $"repos/{_options.Repository}/git/blobs",
            Serialize(new GitHubBlobPayload(Convert.ToBase64String(bytes), "base64"), VisualEvidenceJsonContext.Default.GitHubBlobPayload),
            cancellationToken).ConfigureAwait(false);
        return RequiredString(result.RootElement, "sha");
    }

    private async Task<GitReference?> TryGetReferenceAsync(CancellationToken cancellationToken)
    {
        string branch = Uri.EscapeDataString(_options.AssetsBranch);
        using JsonDocument? result = await TrySendJsonAsync(
            HttpMethod.Get,
            $"repos/{_options.Repository}/git/ref/heads/{branch}",
            null,
            HttpStatusCode.NotFound,
            cancellationToken).ConfigureAwait(false);
        return result is null
            ? null
            : new GitReference(RequiredString(result.RootElement.GetProperty("object"), "sha"));
    }

    private async Task<string> GetCommitTreeAsync(string commitSha, CancellationToken cancellationToken)
    {
        using JsonDocument commit = await SendJsonAsync(
            HttpMethod.Get,
            $"repos/{_options.Repository}/git/commits/{commitSha}",
            null,
            cancellationToken).ConfigureAwait(false);
        return RequiredString(commit.RootElement.GetProperty("tree"), "sha");
    }

    private async Task<string> CreateTreeAsync(
        IReadOnlyList<PendingAsset> entries,
        string? baseTreeSha,
        CancellationToken cancellationToken)
    {
        GitHubTreeEntry[] treeEntries = entries.Select(entry =>
            new GitHubTreeEntry(entry.Path, "100644", "blob", entry.BlobSha)).ToArray();
        var payload = new GitHubTreePayload(baseTreeSha, treeEntries);
        using JsonDocument tree = await SendJsonAsync(
            HttpMethod.Post,
            $"repos/{_options.Repository}/git/trees",
            Serialize(payload, VisualEvidenceJsonContext.Default.GitHubTreePayload),
            cancellationToken).ConfigureAwait(false);
        return RequiredString(tree.RootElement, "sha");
    }

    private async Task<string> CreateCommitAsync(
        string message,
        string treeSha,
        string? parentSha,
        CancellationToken cancellationToken)
    {
        var payload = new GitHubCommitPayload(message, treeSha, parentSha is null ? null : [parentSha]);
        using JsonDocument commit = await SendJsonAsync(
            HttpMethod.Post,
            $"repos/{_options.Repository}/git/commits",
            Serialize(payload, VisualEvidenceJsonContext.Default.GitHubCommitPayload),
            cancellationToken).ConfigureAwait(false);
        return RequiredString(commit.RootElement, "sha");
    }

    private async Task UpdateReferenceAsync(string commitSha, bool create, CancellationToken cancellationToken)
    {
        if (create)
        {
            using JsonDocument created = await SendJsonAsync(
                HttpMethod.Post,
                $"repos/{_options.Repository}/git/refs",
                Serialize(
                    new GitHubRefCreatePayload($"refs/heads/{_options.AssetsBranch}", commitSha),
                    VisualEvidenceJsonContext.Default.GitHubRefCreatePayload),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string branch = Uri.EscapeDataString(_options.AssetsBranch);
        using JsonDocument updated = await SendJsonAsync(
            HttpMethod.Patch,
            $"repos/{_options.Repository}/git/refs/heads/{branch}",
            Serialize(new GitHubRefUpdatePayload(commitSha, false), VisualEvidenceJsonContext.Default.GitHubRefUpdatePayload),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> GetLoginAsync(CancellationToken cancellationToken)
    {
        if (_login is not null)
        {
            return _login;
        }

        if (!string.IsNullOrWhiteSpace(_options.CommentAuthorLogin))
        {
            _login = _options.CommentAuthorLogin.Trim();
            return _login;
        }

        try
        {
            using JsonDocument user = await SendJsonAsync(HttpMethod.Get, "user", null, cancellationToken).ConfigureAwait(false);
            _login = RequiredString(user.RootElement, "login");
        }
        catch (GitHubApiException ex) when (
            ex.StatusCode == HttpStatusCode.Forbidden &&
            ex.Message.Contains("Resource not accessible by integration", StringComparison.OrdinalIgnoreCase))
        {
            // GITHUB_TOKEN is an installation token, which cannot call GET /user.
            _login = "github-actions[bot]";
        }

        return _login;
    }

    private async Task<IReadOnlyList<ExistingComment>> ReadCommentRecordsAsync(
        int changeNumber,
        CancellationToken cancellationToken)
    {
        var comments = new List<ExistingComment>();
        for (int page = 1; ; page++)
        {
            using JsonDocument result = await SendJsonAsync(
                HttpMethod.Get,
                $"repos/{_options.Repository}/issues/{changeNumber}/comments?per_page=100&page={page}",
                null,
                cancellationToken).ConfigureAwait(false);
            JsonElement.ArrayEnumerator items = result.RootElement.EnumerateArray();
            int count = 0;
            foreach (JsonElement item in items)
            {
                count++;
                comments.Add(new ExistingComment(
                    item.GetProperty("id").GetInt64(),
                    item.GetProperty("body").GetString() ?? string.Empty,
                    RequiredString(item.GetProperty("user"), "login")));
            }
            if (count < 100)
            {
                break;
            }
        }
        return comments;
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method,
        string endpoint,
        string? payload,
        CancellationToken cancellationToken)
    {
        JsonDocument? result = await TrySendJsonAsync(
            method,
            endpoint,
            payload,
            null,
            cancellationToken).ConfigureAwait(false);
        return result ?? throw new GitHubApiException(HttpStatusCode.InternalServerError, "GitHub returned no response body.");
    }

    private async Task<JsonDocument?> TrySendJsonAsync(
        HttpMethod method,
        string endpoint,
        string? payload,
        HttpStatusCode? allowedMissingStatus,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, endpoint);
        if (payload is not null)
        {
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
        }
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new GitHubApiException(HttpStatusCode.ServiceUnavailable, "GitHub API request could not be completed.", ex);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GitHubApiException(HttpStatusCode.RequestTimeout, "GitHub API request timed out.", ex);
        }
        using (response)
        {
            if (allowedMissingStatus is not null && response.StatusCode == allowedMissingStatus)
            {
                return null;
            }
            string responseText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                throw new GitHubApiException(
                    response.StatusCode,
                    $"GitHub API request failed ({method} {endpoint}): {SanitizeError(responseText)}");
            }
            try
            {
                return JsonDocument.Parse(string.IsNullOrWhiteSpace(responseText) ? "{}" : responseText);
            }
            catch (JsonException ex)
            {
                throw new GitHubApiException(HttpStatusCode.BadGateway, "GitHub API returned an invalid JSON response.", ex);
            }
        }
    }

    private static string RequiredString(JsonElement element, string property)
    {
        string? value = element.GetProperty(property).GetString();
        return string.IsNullOrWhiteSpace(value)
            ? throw new GitHubApiException(HttpStatusCode.InternalServerError, $"GitHub response omitted '{property}'.")
            : value;
    }

    private static string Serialize<T>(T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.Serialize(payload, typeInfo);

    private static string SanitizeError(string value)
    {
        string compact = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return compact.Length <= 1000 ? compact : compact[..1000];
    }

    private sealed record GitReference(string Sha);

    private sealed record PendingAsset(string Key, string Label, string Path, string BlobSha, bool IsBefore);

    private sealed record ExistingComment(long Id, string Body, string Login);
}

public sealed class GitHubApiException : Exception
{
    public GitHubApiException(HttpStatusCode statusCode, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
