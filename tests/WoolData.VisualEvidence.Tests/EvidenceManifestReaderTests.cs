// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

namespace WoolData.VisualEvidence.Tests;

public sealed class EvidenceManifestReaderTests
{
    [Fact]
    public async Task ReadAsyncRejectsUnknownFields()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "manifest.json");
        try
        {
            await File.WriteAllTextAsync(
                path,
                """
                {
                  "schemaVersion": 1,
                  "snapshot": "after",
                  "revision": "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
                  "environment": {},
                  "captures": [],
                  "unexpected": true
                }
                """,
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<EvidenceValidationException>(() => EvidenceManifestReader.ReadAsync(
                path,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ReadAsyncRejectsOversizedManifest()
    {
        string root = Path.Combine(Path.GetTempPath(), "visual-evidence-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string path = Path.Combine(root, "manifest.json");
        try
        {
            await File.WriteAllBytesAsync(
                path,
                new byte[(1024 * 1024) + 1],
                TestContext.Current.CancellationToken);

            await Assert.ThrowsAsync<EvidenceValidationException>(() => EvidenceManifestReader.ReadAsync(
                path,
                TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
