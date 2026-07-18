// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence;

internal static class SafePngFileEnumerator
{
    public static string[] Enumerate(string root, CancellationToken cancellationToken = default)
    {
        string fullRoot = Path.GetFullPath(root);
        var rootDirectory = new DirectoryInfo(fullRoot);
        if (!rootDirectory.Exists)
        {
            throw new EvidenceValidationException($"Image directory does not exist: {fullRoot}");
        }

        RejectReparsePoint(rootDirectory);
        var directories = new Stack<DirectoryInfo>();
        var files = new List<string>();
        directories.Push(rootDirectory);
        try
        {
            while (directories.TryPop(out DirectoryInfo? directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (FileSystemInfo entry in directory.EnumerateFileSystemInfos())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RejectReparsePoint(entry);
                    if (entry is DirectoryInfo child)
                    {
                        directories.Push(child);
                    }
                    else if (entry is FileInfo file &&
                             string.Equals(file.Extension, ".png", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(file.FullName);
                    }
                }
            }
        }
        catch (EvidenceValidationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new EvidenceValidationException($"Could not enumerate image directory: {fullRoot}", ex);
        }

        return files.Order(StringComparer.Ordinal).ToArray();
    }

    private static void RejectReparsePoint(FileSystemInfo entry)
    {
        if (entry.LinkTarget is not null || entry.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new EvidenceValidationException($"Image input cannot contain a symbolic link or reparse point: {entry.FullName}");
        }
    }
}
