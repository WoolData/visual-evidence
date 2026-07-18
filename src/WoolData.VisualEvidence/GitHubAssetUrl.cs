// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

public static class GitHubAssetUrl
{
    public static void ValidateRepository(string repository) => _ = SplitRepository(repository);

    // Published comments persist this V1 shape. Change it only with a compatibility reader for existing comments.
    public static string Build(string repository, string assetCommit, string path)
    {
        (string owner, string name) = SplitRepository(repository);
        return $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/blob/" +
            $"{assetCommit}/{EscapePath(path)}?raw=true";
    }

    public static string BuildEvidencePrefix(
        string repository,
        string assetCommit,
        int changeNumber,
        string headRevision,
        string snapshot)
    {
        (string owner, string name) = SplitRepository(repository);
        return $"https://github.com/{Uri.EscapeDataString(owner)}/{Uri.EscapeDataString(name)}/blob/" +
            $"{assetCommit}/pr-{changeNumber}/{headRevision}/{snapshot}/";
    }

    private static (string Owner, string Name) SplitRepository(string repository)
    {
        string[] parts = repository.Split('/');
        return parts.Length == 2 && parts.All(part => !string.IsNullOrWhiteSpace(part))
            ? (parts[0], parts[1])
            : throw new ArgumentException("Repository must use owner/name form.", nameof(repository));
    }

    private static string EscapePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
