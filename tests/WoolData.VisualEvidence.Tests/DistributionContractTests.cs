// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Xml.Linq;
using System.Text.Json;

namespace WoolData.VisualEvidence.Tests;

public sealed class DistributionContractTests
{
    [Fact]
    public void ToolPackageHasPublicRegistryMetadata()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(
            root,
            "src",
            "WoolData.VisualEvidence.Cli",
            "WoolData.VisualEvidence.Cli.csproj"));

        Assert.Equal("https://github.com/WoolData/visual-evidence", Value(project, "PackageProjectUrl"));
        Assert.Equal("git", Value(project, "RepositoryType"));
        Assert.Contains("ai-agents", Value(project, "PackageTags"), StringComparison.Ordinal);
        Assert.Equal("false", Value(project, "PackageRequireLicenseAcceptance"));
    }

    [Fact]
    public void NuGetWorkflowVerifiesReleaseAndKeepsApiKeyOffArguments()
    {
        string workflow = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            ".github",
            "workflows",
            "publish-nuget.yml"));

        Assert.Contains("workflow_dispatch", workflow, StringComparison.Ordinal);
        Assert.Contains("environment: nuget-org", workflow, StringComparison.Ordinal);
        Assert.Contains("gh attestation verify", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", workflow, StringComparison.Ordinal);
        Assert.Contains("NUGET_API_KEY: ${{ secrets.NUGET_API_KEY }}", workflow, StringComparison.Ordinal);
        Assert.Contains("10.0.300", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--api-key", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReleaseVersionReferencesStayAligned()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(
            root,
            "src",
            "WoolData.VisualEvidence.Cli",
            "WoolData.VisualEvidence.Cli.csproj"));
        string version = Value(project, "Version");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "tool-manifest.json")));

        Assert.Equal(version, manifest.RootElement.GetProperty("version").GetString());
        foreach (string relativePath in new[]
        {
            "README.md",
            "llms.txt",
            Path.Combine("docs", "DISTRIBUTION.md"),
            Path.Combine(".github", "workflows", "ci.yml"),
            Path.Combine(".github", "workflows", "native-action-canary.yml"),
        })
        {
            Assert.Contains(version, File.ReadAllText(Path.Combine(root, relativePath)), StringComparison.Ordinal);
        }
    }

    private static string Value(XDocument project, string name) =>
        project.Descendants(name).Single().Value;

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "VisualEvidence.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate the Visual Evidence repository root.");
    }
}
