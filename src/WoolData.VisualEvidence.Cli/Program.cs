// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Reflection;
using WoolData.VisualEvidence;
using WoolData.VisualEvidence.GitHub;

return await ProgramMain.RunAsync(args).ConfigureAwait(false);

internal static class ProgramMain
{
    public static async Task<int> RunAsync(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            if (args.Length == 1 && args[0] is "--version" or "-v")
            {
                Console.WriteLine(Version);
                return 0;
            }
            if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
            {
                PrintHelp();
                return 0;
            }

            string command = args[0].ToLowerInvariant();
            var options = OptionReader.Parse(args[1..]);
            return command switch
            {
                "validate" => await ValidateAsync(options, cancellation.Token).ConfigureAwait(false),
                "publish" => await PublishAsync(options, cancellation.Token).ConfigureAwait(false),
                "verify" => await VerifyAsync(options, cancellation.Token).ConfigureAwait(false),
                "doctor" => await DoctorAsync(options, cancellation.Token).ConfigureAwait(false),
                "describe" => Describe(),
                "environment-key" => EnvironmentKey(options),
                "manifest" => await ManifestAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'. Run 'visual-evidence --help'."),
            };
        }
        catch (OperationCanceledException)
        {
            WriteError("canceled", "Canceled.", args.Contains("--json", StringComparer.OrdinalIgnoreCase));
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or EvidenceValidationException or GitHubApiException or IOException)
        {
            string code = ErrorCode(ex);
            WriteError(code, ex.Message, args.Contains("--json", StringComparer.OrdinalIgnoreCase));
            return ex is ArgumentException ? 2 : 1;
        }
    }

    private static string Version =>
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "unknown";

    private static async Task<int> ValidateAsync(OptionReader options, CancellationToken cancellationToken)
    {
        string? root = options.Optional("evidence-root");
        if (root is null)
        {
            string[] images = ResolveImages(options);
            ValidatedImageSet imageSet = await new EvidenceImageSetValidator(BuildValidationOptions(options))
                .ValidateAsync(images, cancellationToken).ConfigureAwait(false);
            WriteJson(
                new AgentValidationResult(true, "images", imageSet.Images.Count),
                AgentProtocolJsonContext.Default.AgentValidationResult);
            return 0;
        }
        var validator = new EvidencePairValidator(BuildValidationOptions(options));
        ValidatedEvidencePair evidence = await validator.ValidateAsync(
            root,
            options.Optional("expected-base"),
            options.Optional("expected-head"),
            cancellationToken).ConfigureAwait(false);
        WriteJson(
            new AgentValidationResult(
                true,
                "comparison",
                evidence.Captures.Count,
                evidence.BeforeManifest.Revision,
                evidence.AfterManifest.Revision,
                evidence.AfterManifest.Environment.CompatibilityKey),
            AgentProtocolJsonContext.Default.AgentValidationResult);
        return 0;
    }

    private static async Task<int> PublishAsync(OptionReader options, CancellationToken cancellationToken)
    {
        string repository = ResolveRepository(options);
        int changeNumber = options.RequiredInt("change-number");
        string token = ResolveToken(options);
        var githubOptions = new GitHubOptions
        {
            Repository = repository,
            Token = token,
            ApiUrl = options.Optional("api-url") ?? Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com",
            AssetsBranch = options.Optional("assets-branch") ?? "visual-evidence-assets",
            CommentAuthorLogin = options.Optional("comment-author-login"),
        };
        using var github = new GitHubApiClient(githubOptions);
        bool publishStatus = options.OptionalBool("publish-status", false);
        var service = new EvidencePublicationService(
            repository,
            github,
            github,
            github,
            publishStatus ? github : null,
            new EvidencePairValidator(BuildValidationOptions(options)),
            github);
        string? evidenceRoot = options.Optional("evidence-root");
        if (evidenceRoot is not null)
        {
            EnsureNoSimpleImages(options);
            AssetPublication publication = await service.PublishAsync(
                changeNumber,
                evidenceRoot,
                options.Required("summary"),
                cancellationToken).ConfigureAwait(false);
            WriteJson(
                new AgentPublishResult(
                    true,
                    "comparison",
                    repository,
                    changeNumber,
                    publication.CommitSha,
                    publication.Assets.Count),
                AgentProtocolJsonContext.Default.AgentPublishResult);
            return 0;
        }

        string[] images = ResolveImages(options);
        ImageAssetPublication imagePublication = await service.PublishImagesAsync(
            changeNumber,
            images,
            options.Required("summary"),
            new EvidenceImageSetValidator(BuildValidationOptions(options)),
            cancellationToken).ConfigureAwait(false);
        WriteJson(
            new AgentPublishResult(
                true,
                "images",
                repository,
                changeNumber,
                imagePublication.CommitSha,
                imagePublication.Assets.Count),
            AgentProtocolJsonContext.Default.AgentPublishResult);
        return 0;
    }

    private static async Task<int> VerifyAsync(OptionReader options, CancellationToken cancellationToken)
    {
        string repository = ResolveRepository(options);
        int changeNumber = options.RequiredInt("change-number");
        using var github = new GitHubApiClient(new GitHubOptions
        {
            Repository = repository,
            Token = ResolveToken(options),
            ApiUrl = options.Optional("api-url") ?? Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com",
            AssetsBranch = options.Optional("assets-branch") ?? "visual-evidence-assets",
            CommentAuthorLogin = options.Optional("comment-author-login"),
        });
        var service = new EvidencePublicationService(repository, github, github, github);
        await service.VerifyAsync(changeNumber, cancellationToken).ConfigureAwait(false);
        WriteJson(
            new AgentVerifyResult(true, repository, changeNumber, true),
            AgentProtocolJsonContext.Default.AgentVerifyResult);
        return 0;
    }

    private static async Task<int> DoctorAsync(OptionReader options, CancellationToken cancellationToken)
    {
        string repository = ResolveRepository(options);
        int changeNumber = options.RequiredInt("change-number");
        using var github = new GitHubApiClient(new GitHubOptions
        {
            Repository = repository,
            Token = ResolveToken(options),
            ApiUrl = options.Optional("api-url") ?? Environment.GetEnvironmentVariable("GITHUB_API_URL") ?? "https://api.github.com",
            AssetsBranch = options.Optional("assets-branch") ?? "visual-evidence-assets",
            CommentAuthorLogin = options.Optional("comment-author-login"),
        });
        ChangeRequestRevision revision = await github.ResolveRevisionAsync(changeNumber, cancellationToken).ConfigureAwait(false);
        WriteJson(
            new AgentDoctorResult(
                true,
                repository,
                changeNumber,
                revision.HeadRevision,
                revision.MergeBaseRevision,
                "accepted"),
            AgentProtocolJsonContext.Default.AgentDoctorResult);
        return 0;
    }

    private static int Describe()
    {
        WriteJson(
            new AgentDescription(
                "visual-evidence",
                Version,
                1,
                "Publish existing PNG images as durable, exact-revision pull-request evidence.",
                true,
                new Dictionary<string, AgentCommandDescription>(StringComparer.Ordinal)
                {
                    ["publish"] = new(["evidence-root", "image-root", "image"], ["summary", "change-number", "repository", "GITHUB_TOKEN"]),
                    ["validate"] = new(["evidence-root", "image-root", "image"]),
                    ["verify"] = new(Requires: ["change-number", "repository", "GITHUB_TOKEN"]),
                    ["doctor"] = new(Requires: ["change-number", "repository", "GITHUB_TOKEN"]),
                    ["manifest"] = new(Purpose: "Build a structured before/after manifest."),
                },
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["success"] = 0,
                    ["failure"] = 1,
                    ["invalidArguments"] = 2,
                    ["canceled"] = 130,
                },
                ["invalid_arguments", "invalid_evidence", "github_api_error", "io_error", "canceled"]),
            AgentProtocolJsonContext.Default.AgentDescription);
        return 0;
    }

    private static int EnvironmentKey(OptionReader options)
    {
        CaptureEnvironment environment = ReadEnvironment(options);
        Console.WriteLine(environment.CalculateCompatibilityKey());
        return 0;
    }

    private static async Task<int> ManifestAsync(OptionReader options, CancellationToken cancellationToken)
    {
        EvidenceManifest manifest = await EvidenceManifestBuilder.BuildAsync(
            options.Required("snapshot").ToLowerInvariant(),
            options.Required("revision"),
            options.Required("capture-root"),
            options.Required("output"),
            ReadEnvironment(options),
            cancellationToken).ConfigureAwait(false);
        WriteJson(
            new AgentManifestResult(
                true,
                options.Required("output"),
                manifest.Captures.Count,
                manifest.Environment.CompatibilityKey),
            AgentProtocolJsonContext.Default.AgentManifestResult);
        return 0;
    }

    private static string[] ResolveImages(OptionReader options)
    {
        string? imageRoot = options.Optional("image-root");
        IReadOnlyList<string> individual = options.All("image");
        if (imageRoot is not null && individual.Count > 0)
        {
            throw new ArgumentException("Use --image-root or repeated --image values, not both.");
        }
        if (imageRoot is not null)
        {
            string root = Path.GetFullPath(imageRoot);
            if (!Directory.Exists(root))
            {
                throw new ArgumentException($"--image-root does not exist: {root}");
            }
            return Directory.GetFiles(root, "*.png", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MatchCasing = MatchCasing.CaseInsensitive,
            });
        }
        return individual.Count > 0
            ? individual.ToArray()
            : throw new ArgumentException("Use --evidence-root, --image-root, or one or more --image values.");
    }

    private static void EnsureNoSimpleImages(OptionReader options)
    {
        if (options.Optional("image-root") is not null || options.All("image").Count > 0)
        {
            throw new ArgumentException("--evidence-root cannot be combined with --image-root or --image.");
        }
    }

    private static void WriteJson<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        Console.WriteLine(JsonSerializer.Serialize(value, typeInfo));

    private static string ErrorCode(Exception ex) => ex switch
    {
        ArgumentException => "invalid_arguments",
        EvidenceValidationException => "invalid_evidence",
        GitHubApiException => "github_api_error",
        IOException => "io_error",
        _ => "unexpected_error",
    };

    private static void WriteError(string code, string message, bool json)
    {
        if (json)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(
                new AgentErrorEnvelope(false, new AgentError(code, message)),
                AgentProtocolJsonContext.Default.AgentErrorEnvelope));
        }
        else
        {
            Console.Error.WriteLine($"visual-evidence [{code}]: {message}");
        }
    }

    private static CaptureEnvironment ReadEnvironment(OptionReader options) => new()
    {
        OperatingSystem = options.Required("os"),
        Architecture = options.Required("architecture"),
        RunnerImage = options.Required("runner-image"),
        CaptureAdapter = options.Required("capture-adapter"),
        AdapterVersion = options.Required("adapter-version"),
        Renderer = options.Required("renderer"),
        RenderScale = options.RequiredDouble("render-scale"),
        FontSetHash = options.Required("font-set-hash"),
        CompatibilityKey = string.Empty,
    };

    private static EvidenceValidationOptions BuildValidationOptions(OptionReader options) => new()
    {
        MaximumImageBytes = options.OptionalLong("maximum-image-bytes", 10 * 1024 * 1024),
        MaximumPixels = options.OptionalLong("maximum-pixels", 40_000_000),
        MaximumCaptureCount = options.OptionalInt("maximum-captures", 50),
        RejectSingleColorImages = !options.OptionalBool("allow-single-color", false),
    };

    private static string ResolveRepository(OptionReader options) =>
        options.Optional("repository") ??
        Environment.GetEnvironmentVariable("GITHUB_REPOSITORY") ??
        throw new ArgumentException("--repository or GITHUB_REPOSITORY is required.");

    private static string ResolveToken(OptionReader options)
    {
        string variable = options.Optional("token-environment-variable") ?? "GITHUB_TOKEN";
        string? token = Environment.GetEnvironmentVariable(variable);
        return string.IsNullOrWhiteSpace(token)
            ? throw new ArgumentException($"Environment variable '{variable}' does not contain a GitHub token.")
            : token;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            visual-evidence - durable before/after evidence for code review

            Commands:
              validate --evidence-root PATH [--expected-base SHA] [--expected-head SHA]
              validate --image PATH [--image PATH ...] | --image-root PATH
              publish  --repository OWNER/REPO --change-number N --evidence-root PATH --summary TEXT
              publish  --repository OWNER/REPO --change-number N --image PATH [--image PATH ...] --summary TEXT
              publish  --repository OWNER/REPO --change-number N --image-root PATH --summary TEXT
              verify   --repository OWNER/REPO --change-number N
              doctor   --repository OWNER/REPO --change-number N
              describe --json
              manifest --snapshot before|after --revision SHA --capture-root PATH --output PATH
                       --os NAME --architecture NAME --runner-image NAME
                       --capture-adapter NAME --adapter-version VERSION
                       --renderer NAME --render-scale N --font-set-hash HASH
              environment-key --os NAME --architecture NAME --runner-image NAME
                              --capture-adapter NAME --adapter-version VERSION
                              --renderer NAME --render-scale N --font-set-hash HASH

            Publish options:
              --assets-branch NAME                 Default: visual-evidence-assets
              --api-url URL                        Default: GITHUB_API_URL or api.github.com
              --comment-author-login LOGIN         Optional custom GitHub App bot login
              --token-environment-variable NAME    Default: GITHUB_TOKEN
              --publish-status true|false          Default: false

            Validation limits:
              --maximum-image-bytes N              Default: 10485760
              --maximum-pixels N                   Default: 40000000
              --maximum-captures N                 Default: 50
              --allow-single-color true|false      Default: false
            """);
    }
}

internal sealed class OptionReader
{
    private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _values;

    private OptionReader(IReadOnlyDictionary<string, IReadOnlyList<string>> values) => _values = values;

    public static OptionReader Parse(string[] args)
    {
        var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length;)
        {
            string name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected '--name value', received '{name}'.");
            }
            string key = name[2..];
            bool flag = key is "json" or "quiet" or "no-color";
            if (!flag && index + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{name}' requires a value.");
            }
            if (!values.TryGetValue(key, out List<string>? entries))
            {
                entries = new List<string>();
                values.Add(key, entries);
            }
            if (key != "image" && entries.Count > 0)
            {
                throw new ArgumentException($"Option '{name}' was provided more than once.");
            }
            entries.Add(flag ? "true" : args[index + 1]);
            index += flag ? 1 : 2;
        }
        return new OptionReader(values.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.OrdinalIgnoreCase));
    }

    public string Required(string name) => Optional(name) ?? throw new ArgumentException($"--{name} is required.");

    public string? Optional(string name) => _values.TryGetValue(name, out IReadOnlyList<string>? values) ? values[^1] : null;

    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out IReadOnlyList<string>? values) ? values : Array.Empty<string>();

    public int RequiredInt(string name) => ParseInt(name, Required(name));

    public int OptionalInt(string name, int fallback) => Optional(name) is { } value ? ParseInt(name, value) : fallback;

    public long OptionalLong(string name, long fallback) => Optional(name) is { } value ? ParseLong(name, value) : fallback;

    public bool OptionalBool(string name, bool fallback) => Optional(name) is { } value
        ? bool.TryParse(value, out bool parsed)
            ? parsed
            : throw new ArgumentException($"--{name} must be true or false.")
        : fallback;

    public double RequiredDouble(string name) =>
        double.TryParse(
            Required(name),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed) && parsed > 0 && double.IsFinite(parsed)
            ? parsed
            : throw new ArgumentException($"--{name} must be a positive finite number.");

    private static int ParseInt(string name, string value) =>
        int.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"--{name} must be a positive integer.");

    private static long ParseLong(string name, string value) =>
        long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out long parsed) && parsed > 0
            ? parsed
            : throw new ArgumentException($"--{name} must be a positive integer.");
}
