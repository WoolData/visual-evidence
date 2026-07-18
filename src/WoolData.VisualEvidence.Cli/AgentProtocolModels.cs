// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json.Serialization;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
[JsonSerializable(typeof(AgentValidationResult))]
[JsonSerializable(typeof(AgentPublishResult))]
[JsonSerializable(typeof(AgentVerifyResult))]
[JsonSerializable(typeof(AgentDoctorResult))]
[JsonSerializable(typeof(AgentManifestResult))]
[JsonSerializable(typeof(AgentDescription))]
[JsonSerializable(typeof(AgentErrorEnvelope))]
internal sealed partial class AgentProtocolJsonContext : JsonSerializerContext;

internal sealed record AgentValidationResult(
    bool Ok,
    string Mode,
    int Captures,
    string? Before = null,
    string? After = null,
    string? CompatibilityKey = null);

internal sealed record AgentPublishResult(
    bool Ok,
    string Mode,
    string Repository,
    int ChangeNumber,
    string AssetCommit,
    int Captures);

internal sealed record AgentVerifyResult(bool Ok, string Repository, int ChangeNumber, bool Current);

internal sealed record AgentDoctorResult(
    bool Ok,
    string Repository,
    int ChangeNumber,
    string Head,
    string MergeBase,
    string Authentication);

internal sealed record AgentManifestResult(bool Ok, string Output, int Captures, string CompatibilityKey);

internal sealed record AgentDescription(
    string Name,
    string Version,
    int ProtocolVersion,
    string Purpose,
    bool CaptureIsExternal,
    IReadOnlyDictionary<string, AgentCommandDescription> Commands,
    IReadOnlyDictionary<string, int> ExitCodes,
    IReadOnlyList<string> ErrorCodes);

internal sealed record AgentCommandDescription(
    IReadOnlyList<string>? Modes = null,
    IReadOnlyList<string>? Requires = null,
    string? Purpose = null);

internal sealed record AgentErrorEnvelope(bool Ok, AgentError Error);

internal sealed record AgentError(string Code, string Message);
