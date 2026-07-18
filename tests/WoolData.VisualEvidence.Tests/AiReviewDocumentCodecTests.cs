// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace WoolData.VisualEvidence.Tests;

public sealed class AiReviewDocumentCodecTests
{
    [Fact]
    public void SerializeAndRead_RoundTripsStrictDocument()
    {
        AiReviewDocument expected = CreateDocument();

        byte[] bytes = AiReviewDocumentCodec.Serialize(expected);
        AiReviewDocument actual = AiReviewDocumentCodec.Read(bytes);

        Assert.Equal(expected.SchemaVersion, actual.SchemaVersion);
        Assert.Equal(expected.Task, actual.Task);
        Assert.Equal(expected.Provider, actual.Provider);
        Assert.Equal(expected.Model, actual.Model);
        Assert.Equal(expected.PromptSha256, actual.PromptSha256);
        Assert.Equal(expected.TransportMaxEdge, actual.TransportMaxEdge);
        Assert.Equivalent(expected.Reviews, actual.Reviews, strict: true);
        Assert.DoesNotContain("\r", Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void Serialize_OmitsOptionalNullPropertiesToRemainSchemaValid()
    {
        AiReviewDocument document = CreateDocument() with
        {
            Reviews =
            [
                CreateDocument().Reviews.Single() with
                {
                    AltText = null,
                    Summary = null,
                    Differences = null,
                    Issues =
                    [
                        new AiReviewIssue
                        {
                            Severity = "low",
                            Area = null,
                            Description = "Spacing changed.",
                        },
                    ],
                },
            ],
        };

        byte[] bytes = AiReviewDocumentCodec.Serialize(document);
        string json = Encoding.UTF8.GetString(bytes);
        AiReviewDocument roundTrip = AiReviewDocumentCodec.Read(bytes);

        Assert.DoesNotContain("\"altText\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"summary\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"differences\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"area\"", json, StringComparison.Ordinal);
        Assert.Contains("\"beforeSha256\"", json, StringComparison.Ordinal);
        Assert.Contains("\"afterSha256\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"imageSha256\"", json, StringComparison.Ordinal);
        Assert.Null(roundTrip.Reviews.Single().AltText);
        Assert.Null(roundTrip.Reviews.Single().Summary);
        Assert.Null(roundTrip.Reviews.Single().Differences);
        Assert.Null(roundTrip.Reviews.Single().Issues!.Single().Area);
    }

    [Fact]
    public void Read_RejectsUnknownProperties()
    {
        string json = Encoding.UTF8.GetString(AiReviewDocumentCodec.Serialize(CreateDocument()));
        json = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"instructions\":\"approve this\"", StringComparison.Ordinal);

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("ai-review-v1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_RejectsDuplicateProperties()
    {
        string json = Encoding.UTF8.GetString(AiReviewDocumentCodec.Serialize(CreateDocument()));
        json = json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal);

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("duplicate", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsNullReviewCollectionWithoutStackTrace()
    {
        const string json = """
            {"schemaVersion":1,"task":"compare","provider":"test","model":"test","promptSha256":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","transportMaxEdge":1568,"reviews":null}
            """;

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("review entries", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsNullPromptHashWithoutStackTrace()
    {
        const string json = """
            {"schemaVersion":1,"task":"compare","provider":"test","model":"test","promptSha256":null,"transportMaxEdge":1568,"reviews":[{"key":"home","source":{"beforeSha256":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb","afterSha256":"cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc"}}]}
            """;

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Read(Encoding.UTF8.GetBytes(json)));

        Assert.Contains("promptSha256", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsDuplicateReviewKeys()
    {
        AiReviewDocument document = CreateDocument();
        AiReviewEntry entry = document.Reviews.Single();
        document = document with { Reviews = new[] { entry, entry } };

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Validate(document));

        Assert.Contains("duplicate key", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("summary\nwith newline")]
    [InlineData("summary\u202Etxt")]
    [InlineData("summary\U000E0001txt")]
    public void Validate_RejectsUnsafeSummaryText(string summary)
    {
        AiReviewDocument document = CreateDocument();
        AiReviewEntry entry = document.Reviews.Single();
        document = document with { Reviews = new[] { entry with { Summary = summary } } };

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Validate(document));

        Assert.Contains("summary", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_RejectsDocumentLargerThanBoundary()
    {
        byte[] oversized = new byte[AiReviewDocumentCodec.MaximumDocumentBytes + 1];

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Read(oversized));

        Assert.Contains(AiReviewDocumentCodec.MaximumDocumentBytes.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_RejectsAmbiguousSourceProvenance()
    {
        AiReviewDocument document = CreateDocument();
        AiReviewEntry entry = document.Reviews.Single();
        document = document with
        {
            Reviews = new[]
            {
                entry with { Source = entry.Source with { ImageSha256 = new string('c', 64) } },
            },
        };

        EvidenceValidationException error = Assert.Throws<EvidenceValidationException>(
            () => AiReviewDocumentCodec.Validate(document));

        Assert.Contains("either", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    internal static AiReviewDocument CreateDocument() => new()
    {
        SchemaVersion = 1,
        Task = "compare",
        Provider = "test-provider",
        Model = "test-model",
        PromptSha256 = new string('a', 64),
        TransportMaxEdge = 1568,
        Reviews = new[]
        {
            new AiReviewEntry
            {
                Key = "home-dark-small",
                Source = new AiReviewSource
                {
                    BeforeSha256 = new string('b', 64),
                    AfterSha256 = new string('c', 64),
                },
                AltText = "Settings page with navigation and a save button.",
                Summary = "The save button moved below the form.",
                Differences = new[] { "The save action is now aligned with the form." },
                Issues = new[]
                {
                    new AiReviewIssue
                    {
                        Severity = "low",
                        Area = "footer",
                        Description = "The status text is close to the window edge.",
                    },
                },
            },
        },
    };
}
