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
}

[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;
