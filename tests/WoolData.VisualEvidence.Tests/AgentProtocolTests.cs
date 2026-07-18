// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;

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
}

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;
