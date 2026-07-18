// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoolData.VisualEvidence.Tests;

public sealed class SchemaContractTests
{
    [Fact]
    public void PublishedSchemaContainsOnlyValidRegularExpressions()
    {
        string schemaPath = Path.Combine(FindRepositoryRoot(), "schema", "evidence-manifest-v1.schema.json");
        using JsonDocument schema = JsonDocument.Parse(File.ReadAllText(schemaPath));

        foreach (string pattern in FindPatterns(schema.RootElement))
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
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
