// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Text;

namespace WoolData.VisualEvidence.Tests;

[Collection("Console")]
public sealed class AgentProtocolTests
{
    [Fact]
    public async Task Describe_ReturnsCompactMachineReadableContract()
    {
        TextWriter original = Console.Out;
        using var output = new StringWriter();
        try
        {
            Console.SetOut(output);
            int exitCode = await ProgramMain.RunAsync(["describe", "--json"]);

            Assert.Equal(0, exitCode);
            string json = output.ToString().Trim();
            Assert.DoesNotContain(Environment.NewLine, json, StringComparison.Ordinal);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.Equal("visual-evidence", document.RootElement.GetProperty("name").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("protocolVersion").GetInt32());
            Assert.True(document.RootElement.GetProperty("captureIsExternal").GetBoolean());
            Assert.Equal("GITHUB_TOKEN", document.RootElement.GetProperty("tokenEnvironmentVariable").GetString());
            Assert.True(document.RootElement.GetProperty("republishIsIdempotent").GetBoolean());
            Assert.Equal(
                5,
                document.RootElement.GetProperty("environmentVariables").EnumerateObject().Count());
            Assert.Equal(4, document.RootElement.GetProperty("workflow").GetArrayLength());
            JsonElement.ArrayEnumerator publishOptions = document.RootElement
                .GetProperty("commands")
                .GetProperty("publish")
                .GetProperty("options")
                .EnumerateArray();
            string[] optionNames = publishOptions.Select(static option => option.GetString()!).ToArray();
            Assert.Contains("--summary", optionNames);
            Assert.Equal(optionNames.Length, optionNames.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.InRange(Encoding.UTF8.GetByteCount(json), 1, 4096);
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task InvalidArguments_ReturnCompactJsonErrorAndStableExitCode()
    {
        TextWriter original = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            int exitCode = await ProgramMain.RunAsync(["validate", "--json"]);

            Assert.Equal(2, exitCode);
            string json = output.ToString().Trim();
            Assert.True(json.Length < 500);
            using JsonDocument document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal("invalid_arguments", document.RootElement.GetProperty("error").GetProperty("code").GetString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task UnknownOption_IsRejectedInsteadOfSilentlyIgnored()
    {
        TextWriter original = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            int exitCode = await ProgramMain.RunAsync(["validate", "--maximum-image-byte", "1", "--json"]);

            Assert.Equal(2, exitCode);
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Equal("invalid_arguments", document.RootElement.GetProperty("error").GetProperty("code").GetString());
            Assert.Contains("--maximum-image-byte", document.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task UnknownCommand_IsRejectedBeforeItsOptionsAreParsed()
    {
        TextWriter original = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            int exitCode = await ProgramMain.RunAsync(["frobnicate", "--x", "y", "--json"]);

            Assert.Equal(2, exitCode);
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Contains("Unknown command 'frobnicate'", document.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task OptionValue_CannotConsumeFollowingOption()
    {
        TextWriter original = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            int exitCode = await ProgramMain.RunAsync(["publish", "--summary", "--json"]);

            Assert.Equal(2, exitCode);
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Contains("requires a value", document.RootElement.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task EnvironmentKey_SupportsStructuredOutputWithoutChangingPlainOutput()
    {
        string[] arguments =
        [
            "environment-key",
            "--os", "macos",
            "--architecture", "arm64",
            "--runner-image", "macos-15-arm64",
            "--capture-adapter", "avalonia-headless",
            "--adapter-version", "1.0.0",
            "--renderer", "skia",
            "--render-scale", "1",
            "--font-set-hash", "bundled-fonts-v1",
        ];
        TextWriter original = Console.Out;
        using var plainOutput = new StringWriter();
        using var jsonOutput = new StringWriter();
        try
        {
            Console.SetOut(plainOutput);
            Assert.Equal(0, await ProgramMain.RunAsync(arguments));
            Console.SetOut(jsonOutput);
            Assert.Equal(0, await ProgramMain.RunAsync([.. arguments, "--json"]));

            string key = plainOutput.ToString().Trim();
            using JsonDocument document = JsonDocument.Parse(jsonOutput.ToString());
            Assert.True(document.RootElement.GetProperty("ok").GetBoolean());
            Assert.Equal(key, document.RootElement.GetProperty("compatibilityKey").GetString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public async Task Publish_RejectsOverlongSummaryBeforeGitHubAccess()
    {
        TextWriter original = Console.Error;
        using var output = new StringWriter();
        try
        {
            Console.SetError(output);
            int exitCode = await ProgramMain.RunAsync([
                "publish",
                "--summary", new string('a', 2001),
                "--repository", "WoolData/example",
                "--change-number", "1",
                "--image", "missing.png",
                "--json",
            ]);

            Assert.Equal(2, exitCode);
            Assert.Contains("no longer than 2000", output.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public async Task Review_RequiresExplicitProviderWhenBothDefaultKeysExist()
    {
        using var fixture = new EvidenceFixture();
        string? originalAnthropic = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        string? originalOpenAi = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        TextWriter originalError = Console.Error;
        using var output = new StringWriter();
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", "anthropic-secret");
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", "openai-secret");
            Console.SetError(output);

            int exitCode = await ProgramMain.RunAsync([
                "review",
                "--evidence-root", fixture.Root,
                "--output", Path.Combine(fixture.Root, "review.json"),
                "--ai-model", "test-model",
                "--json",
            ]);

            Assert.Equal(2, exitCode);
            using JsonDocument document = JsonDocument.Parse(output.ToString());
            Assert.Contains(
                "specify --ai-provider",
                document.RootElement.GetProperty("error").GetProperty("message").GetString(),
                StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(originalError);
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", originalAnthropic);
            Environment.SetEnvironmentVariable("OPENAI_API_KEY", originalOpenAi);
        }
    }

    [Fact]
    public void BundledSkill_StaysWithinAgentContextBudget()
    {
        string path = Path.Combine(FindRepositoryRoot(), "skills", "visual-evidence", "SKILL.md");
        string skill = File.ReadAllText(path);

        Assert.InRange(Encoding.UTF8.GetByteCount(skill), 1, 3000);
        Assert.Contains("visual-evidence describe --json", skill, StringComparison.Ordinal);
        Assert.Contains("GITHUB_TOKEN", skill, StringComparison.Ordinal);
    }

    [Fact]
    public void LlmsIndex_StaysCompactAndPointsToMachineReadableDiscovery()
    {
        string path = Path.Combine(FindRepositoryRoot(), "llms.txt");
        string index = File.ReadAllText(path);

        Assert.InRange(Encoding.UTF8.GetByteCount(index), 1, 4096);
        Assert.Contains("visual-evidence describe --json", index, StringComparison.Ordinal);
        Assert.Contains("skills/visual-evidence/SKILL.md", index, StringComparison.Ordinal);
        Assert.Contains("schema/agent-protocol-v1.json", index, StringComparison.Ordinal);
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

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;
