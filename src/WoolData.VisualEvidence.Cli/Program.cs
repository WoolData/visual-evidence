// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
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
                "environment-key" => EnvironmentKey(options),
                "manifest" => await ManifestAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'. Run 'visual-evidence --help'."),
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Canceled.");
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or EvidenceValidationException or GitHubApiException or IOException)
        {
            Console.Error.WriteLine($"visual-evidence: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> ValidateAsync(OptionReader options, CancellationToken cancellationToken)
    {
        string root = options.Required("evidence-root");
        var validator = new EvidencePairValidator(BuildValidationOptions(options));
        ValidatedEvidencePair evidence = await validator.ValidateAsync(
            root,
            options.Optional("expected-base"),
            options.Optional("expected-head"),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            valid = true,
            before = evidence.BeforeManifest.Revision,
            after = evidence.AfterManifest.Revision,
            compatibilityKey = evidence.AfterManifest.Environment.CompatibilityKey,
            captures = evidence.Captures.Count,
        }, new JsonSerializerOptions { WriteIndented = true }));
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
            new EvidencePairValidator(BuildValidationOptions(options)));
        AssetPublication publication = await service.PublishAsync(
            changeNumber,
            options.Required("evidence-root"),
            options.Required("summary"),
            cancellationToken).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            published = true,
            repository,
            changeNumber,
            assetCommit = publication.CommitSha,
            captures = publication.Assets.Count,
        }, new JsonSerializerOptions { WriteIndented = true }));
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
        Console.WriteLine($"Visual evidence for {repository}#{changeNumber} is current.");
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
        Console.WriteLine($"Wrote {manifest.Captures.Count} captures to {options.Required("output")}.");
        Console.WriteLine($"Compatibility key: {manifest.Environment.CompatibilityKey}");
        return 0;
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
              publish  --repository OWNER/REPO --change-number N --evidence-root PATH --summary TEXT
              verify   --repository OWNER/REPO --change-number N
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
    private readonly IReadOnlyDictionary<string, string> _values;

    private OptionReader(IReadOnlyDictionary<string, string> values) => _values = values;

    public static OptionReader Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < args.Length; index += 2)
        {
            string name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value', received '{name}'.");
            }
            if (!values.TryAdd(name[2..], args[index + 1]))
            {
                throw new ArgumentException($"Option '{name}' was provided more than once.");
            }
        }
        return new OptionReader(values);
    }

    public string Required(string name) => Optional(name) ?? throw new ArgumentException($"--{name} is required.");

    public string? Optional(string name) => _values.TryGetValue(name, out string? value) ? value : null;

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
