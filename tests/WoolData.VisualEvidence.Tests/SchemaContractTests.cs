// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoolData.VisualEvidence.Tests;

public sealed class SchemaContractTests
{
    [Fact]
    public void PublishedSchemaContainsOnlyValidRegularExpressions()
    {
        string schemaRoot = Path.Combine(FindRepositoryRoot(), "schema");
        foreach (string schemaPath in Directory.GetFiles(schemaRoot, "*.schema.json"))
        {
            using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
            foreach (string pattern in FindPatterns(schema.RootElement))
            {
                _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
        }
    }

    [Fact]
    public void AiReviewSchemaPinsStrictVersionedDocumentShape()
    {
        string schemaPath = Path.Combine(FindRepositoryRoot(), "schema", "ai-review-v1.schema.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        Assert.Equal(
            "https://github.com/WoolData/visual-evidence/schema/ai-review-v1.schema.json",
            schema.RootElement.GetProperty("$id").GetString());
        Assert.False(schema.RootElement.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(1, schema.RootElement
            .GetProperty("properties")
            .GetProperty("schemaVersion")
            .GetProperty("const")
            .GetInt32());
        Assert.Equal(50, schema.RootElement
            .GetProperty("properties")
            .GetProperty("reviews")
            .GetProperty("maxItems")
            .GetInt32());
        string[] requiredReviewProperties = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("review")
            .GetProperty("required")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        Assert.DoesNotContain("altText", requiredReviewProperties);
        Assert.DoesNotContain("summary", requiredReviewProperties);
        Assert.DoesNotContain("differences", requiredReviewProperties);
        Assert.DoesNotContain("issues", requiredReviewProperties);
        string[] requiredIssueProperties = schema.RootElement
            .GetProperty("$defs")
            .GetProperty("issue")
            .GetProperty("required")
            .EnumerateArray()
            .Select(static item => item.GetString()!)
            .ToArray();
        Assert.DoesNotContain("area", requiredIssueProperties);
    }

    [Fact]
    public void PublishedSchemaAcceptsUppercasePngPathsLikeTheRuntime()
    {
        string schemaPath = Path.Combine(FindRepositoryRoot(), "schema", "evidence-manifest-v1.schema.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));
        string pattern = schema.RootElement
            .GetProperty("properties")
            .GetProperty("captures")
            .GetProperty("items")
            .GetProperty("properties")
            .GetProperty("path")
            .GetProperty("pattern")
            .GetString()!;

        Assert.Matches(new Regex(pattern, RegexOptions.CultureInvariant), "captures/Home.PNG");
    }

    [Fact]
    public void AgentProtocolAdvertisesOnlyImplementedReviewTask()
    {
        string schemaPath = Path.Combine(FindRepositoryRoot(), "schema", "agent-protocol-v1.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        Assert.Equal(
            "compare",
            schema.RootElement.GetProperty("properties").GetProperty("task").GetProperty("const").GetString());
    }

    private static IEnumerable<string> FindPatterns(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.NameEquals("pattern") && property.Value.ValueKind == JsonValueKind.String)
                {
                    yield return property.Value.GetString()!;
                }

                foreach (string pattern in FindPatterns(property.Value))
                {
                    yield return pattern;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                foreach (string pattern in FindPatterns(item))
                {
                    yield return pattern;
                }
            }
        }
    }

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
