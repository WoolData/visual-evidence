// Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

using System.Text.Json;
using System.Reflection;
using System.Diagnostics;
using WoolData.VisualEvidence;
using WoolData.VisualEvidence.GitHub;

return await ProgramMain.RunAsync(args).ConfigureAwait(false);

internal static class ProgramMain
{
    private static readonly AiProviderProfile[] AiProviderProfiles =
    [
        new("anthropic", "ANTHROPIC_API_KEY", "https://api.anthropic.com/", AiProviderProtocolKind.Anthropic, false),
        new("openai-compatible", "OPENAI_API_KEY", "https://api.openai.com/v1/", AiProviderProtocolKind.OpenAiCompatible, true),
        new("grok", "XAI_API_KEY", "https://api.x.ai/v1/", AiProviderProtocolKind.XaiResponses, false),
        new("gemini", "GEMINI_API_KEY", "https://generativelanguage.googleapis.com/v1beta/openai/", AiProviderProtocolKind.OpenAiCompatible, false),
    ];

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
            if (command is not ("validate" or "review" or "publish" or "verify" or "doctor" or "describe" or "environment-key" or "manifest"))
            {
                throw new ArgumentException($"Unknown command '{args[0]}'. Run 'visual-evidence --help'.");
            }
            var options = OptionReader.Parse(args[1..]);
            EnsureAllowedOptions(command, options);
            return command switch
            {
                "validate" => await ValidateAsync(options, cancellation.Token).ConfigureAwait(false),
                "review" => await ReviewAsync(options, cancellation.Token).ConfigureAwait(false),
                "publish" => await PublishAsync(options, cancellation.Token).ConfigureAwait(false),
                "verify" => await VerifyAsync(options, cancellation.Token).ConfigureAwait(false),
                "doctor" => await DoctorAsync(options, cancellation.Token).ConfigureAwait(false),
                "describe" => Describe(),
                "environment-key" => EnvironmentKey(options),
                "manifest" => await ManifestAsync(options, cancellation.Token).ConfigureAwait(false),
                _ => throw new UnreachableException(),
            };
        }
        catch (OperationCanceledException)
        {
            WriteError("canceled", "Canceled.", args.Contains("--json", StringComparer.OrdinalIgnoreCase));
            return 130;
        }
        catch (Exception ex) when (ex is ArgumentException or EvidenceValidationException or AiReviewProviderException or GitHubApiException or IOException)
        {
            string code = ErrorCode(ex);
            WriteError(code, ex.Message, args.Contains("--json", StringComparer.OrdinalIgnoreCase));
            return ex is ArgumentException ? 2 : 1;
        }
        catch (Exception)
        {
            WriteError("unexpected_error", "Unexpected failure.", args.Contains("--json", StringComparer.OrdinalIgnoreCase));
            return 1;
        }
    }

    private static async Task<int> ReviewAsync(OptionReader options, CancellationToken cancellationToken)
    {
        string task = (options.Optional("task") ?? "compare").ToLowerInvariant();
        if (task != "compare")
        {
            throw new ArgumentException("--task currently supports compare only.");
        }

        string evidenceRoot = options.Required("evidence-root");
        string output = Path.GetFullPath(options.Required("output"));
        string prompt = ReadPrompt(options.Optional("prompt-file"));
        int maximumEdge = options.OptionalInt("ai-max-edge", AiReviewTransportImageFactory.DefaultMaximumEdge);
        ValidatedEvidencePair evidence = await new EvidencePairValidator(BuildValidationOptions(options)).ValidateAsync(
            evidenceRoot,
            options.Optional("expected-base"),
            options.Optional("expected-head"),
            cancellationToken).ConfigureAwait(false);
        string promptHash = AiReviewPrompt.CalculateSha256(prompt);
        var request = new AiReviewRequest(
            task,
            prompt,
            promptHash,
            maximumEdge,
            AiReviewTransportImageFactory.CreateComparison(evidence, maximumEdge));

        string providerName = ResolveAiProvider(options);
        AiProviderProfile profile = ResolveAiProviderProfile(providerName);
        AiReviewDocument review;
        if (profile.Protocol == AiProviderProtocolKind.Anthropic)
        {
            if (options.OptionalBool("ai-no-auth", false))
            {
                throw new ArgumentException("--ai-no-auth is supported only with --ai-provider openai-compatible.");
            }
            using var provider = new AnthropicImageReviewProvider(new AnthropicImageReviewOptions
            {
                ApiKey = ResolveAiKey(options, profile.KeyEnvironmentVariable),
                Model = options.Required("ai-model"),
                BaseUri = ResolveAiBaseUri(options, profile, noAuth: false),
            });
            review = await provider.ReviewAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else if (profile.Protocol == AiProviderProtocolKind.XaiResponses)
        {
            if (options.OptionalBool("ai-no-auth", false))
            {
                throw new ArgumentException("--ai-no-auth is supported only with --ai-provider openai-compatible.");
            }
            using var provider = new GrokImageReviewProvider(new GrokImageReviewOptions
            {
                ApiKey = ResolveAiKey(options, profile.KeyEnvironmentVariable),
                Model = options.Required("ai-model"),
                BaseUri = ResolveAiBaseUri(options, profile, noAuth: false),
            });
            review = await provider.ReviewAsync(request, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            bool noAuth = options.OptionalBool("ai-no-auth", false);
            Uri baseUri = ResolveAiBaseUri(options, profile, noAuth);
            if (noAuth && (!profile.AllowsNoAuth || !baseUri.IsLoopback))
            {
                throw new ArgumentException("--ai-no-auth is allowed only with --ai-provider openai-compatible and a loopback --ai-base-url.");
            }
            using var provider = new OpenAiCompatibleImageReviewProvider(new OpenAiCompatibleImageReviewOptions
            {
                ApiKey = noAuth ? null : ResolveAiKey(options, profile.KeyEnvironmentVariable),
                Model = options.Required("ai-model"),
                ProviderName = providerName,
                BaseUri = baseUri,
            });
            review = await provider.ReviewAsync(request, cancellationToken).ConfigureAwait(false);
        }

        AiReviewProvenanceValidator.ValidateComparison(review, evidence);
        await WriteFileAtomicallyAsync(output, AiReviewDocumentCodec.Serialize(review), cancellationToken).ConfigureAwait(false);
        WriteJson(
            new AgentReviewResult(
                true,
                review.Task,
                review.Provider,
                review.Model,
                output,
                review.Reviews.Count,
                review.PromptSha256),
            AgentProtocolJsonContext.Default.AgentReviewResult);
        return 0;
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
            ValidatedImageSet imageSet = await new EvidenceImageSetValidator(
                    BuildValidationOptions(options),
                    options.Optional("image-root"))
                .ValidateAsync(images, cancellationToken).ConfigureAwait(false);
            WriteJson(
                new AgentValidationResult(true, "images", imageSet.Images.Count),
                AgentProtocolJsonContext.Default.AgentValidationResult);
            return 0;
        }
        EnsureNoSimpleImages(options);
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
        string summary = options.RequiredLimited("summary", 2000);
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
            string? aiReview = options.Optional("ai-review");
            AssetPublication publication = aiReview is null
                ? await service.PublishAsync(
                    changeNumber,
                    evidenceRoot,
                    summary,
                    cancellationToken).ConfigureAwait(false)
                : await service.PublishWithAiReviewAsync(
                    changeNumber,
                    evidenceRoot,
                    aiReview,
                    summary,
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

        if (options.Optional("ai-review") is not null)
        {
            throw new ArgumentException("--ai-review currently requires manifest-backed --evidence-root comparison mode.");
        }

        string[] images = ResolveImages(options);
        ImageAssetPublication imagePublication = await service.PublishImagesAsync(
            changeNumber,
            images,
            summary,
            new EvidenceImageSetValidator(BuildValidationOptions(options), options.Optional("image-root")),
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
                "Validate and publish PNGs as exact-revision pull-request evidence, with optional advisory AI review.",
                true,
                "GITHUB_TOKEN",
                true,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["GITHUB_TOKEN"] = "Required for doctor, publish, and verify; never pass tokens as arguments.",
                    ["GITHUB_REPOSITORY"] = "Optional OWNER/REPO default for --repository.",
                    ["GITHUB_API_URL"] = "Optional API endpoint default for --api-url.",
                    ["ANTHROPIC_API_KEY"] = "Default Anthropic review credential; never pass keys as arguments.",
                    ["OPENAI_API_KEY"] = "Default OpenAI-compatible review credential; never pass keys as arguments.",
                    ["XAI_API_KEY"] = "Default Grok review credential; never pass keys as arguments.",
                    ["GEMINI_API_KEY"] = "Default Gemini review credential; never pass keys as arguments.",
                },
                new Dictionary<string, AgentCommandDescription>(StringComparer.Ordinal)
                {
                    ["publish"] = new(["evidence-root", "image-root", "image"], ["summary", "change-number", "GITHUB_TOKEN"], DescribeOptions("publish")),
                    ["validate"] = new(["evidence-root", "image-root", "image"], Options: DescribeOptions("validate")),
                    ["review"] = new(["evidence-root"], ["evidence-root", "output", "ai-model"], DescribeOptions("review"), "Produce optional advisory ai-review-v1 JSON from validated evidence."),
                    ["verify"] = new(Requires: ["change-number", "GITHUB_TOKEN"], Options: DescribeOptions("verify")),
                    ["doctor"] = new(Requires: ["change-number", "GITHUB_TOKEN"], Options: DescribeOptions("doctor")),
                    ["manifest"] = new(Options: DescribeOptions("manifest"), Purpose: "Build a structured before/after manifest."),
                    ["environment-key"] = new(Options: DescribeOptions("environment-key"), Purpose: "Calculate a capture-environment compatibility key."),
                },
                [
                    "visual-evidence doctor --repository OWNER/REPO --change-number N --json",
                    "visual-evidence review --evidence-root PATH --output ai-review-v1.json --ai-model MODEL --json",
                    "visual-evidence publish --repository OWNER/REPO --change-number N --evidence-root PATH --ai-review ai-review-v1.json --summary TEXT --json",
                    "visual-evidence verify --repository OWNER/REPO --change-number N --json",
                ],
                new Dictionary<string, int>(StringComparer.Ordinal)
                {
                    ["success"] = 0,
                    ["failure"] = 1,
                    ["invalidArguments"] = 2,
                    ["canceled"] = 130,
                },
                ["invalid_arguments", "invalid_evidence", "ai_provider_error", "github_api_error", "io_error", "unexpected_error", "canceled"]),
            AgentProtocolJsonContext.Default.AgentDescription);
        return 0;
    }

    private static int EnvironmentKey(OptionReader options)
    {
        CaptureEnvironment environment = ReadEnvironment(options);
        string key = environment.CalculateCompatibilityKey();
        if (options.OptionalBool("json", false))
        {
            WriteJson(
                new AgentEnvironmentKeyResult(true, key),
                AgentProtocolJsonContext.Default.AgentEnvironmentKeyResult);
        }
        else
        {
            Console.WriteLine(key);
        }
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
            BuildValidationOptions(options),
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
            return EvidenceImageSetValidator.EnumerateDirectory(root);
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

    private static void EnsureAllowedOptions(string command, OptionReader options)
    {
        options.EnsureOnly(command, AllowedOptions(command));
    }

    private static string[] AllowedOptions(string command)
    {
        string[] commonValidation = ["maximum-image-bytes", "maximum-pixels", "maximum-captures", "allow-single-color", "json"];
        string[] environment = ["os", "architecture", "runner-image", "capture-adapter", "adapter-version", "renderer", "render-scale", "font-set-hash"];
        string[] github = ["repository", "change-number", "assets-branch", "comment-author-login", "token-environment-variable", "api-url", "json"];

        return command switch
        {
            "validate" => ["evidence-root", "expected-base", "expected-head", "image-root", "image", .. commonValidation],
            "review" => ["evidence-root", "expected-base", "expected-head", "output", "task", "ai-provider", "ai-model", "ai-base-url", "ai-allow-custom-egress", "ai-key-environment-variable", "ai-no-auth", "ai-max-edge", "prompt-file", .. commonValidation],
            "publish" => ["evidence-root", "image-root", "image", "ai-review", "summary", "publish-status", .. github, .. commonValidation],
            "verify" or "doctor" => github,
            "describe" => ["json"],
            "environment-key" => [.. environment, "json"],
            "manifest" => ["snapshot", "revision", "capture-root", "output", .. environment, .. commonValidation],
            _ => [],
        };
    }

    private static string[] DescribeOptions(string command) =>
        AllowedOptions(command)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(static option => $"--{option}")
            .ToArray();

    private static void WriteJson<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        Console.WriteLine(JsonSerializer.Serialize(value, typeInfo));

    private static string ErrorCode(Exception ex) => ex switch
    {
        ArgumentException => "invalid_arguments",
        EvidenceValidationException => "invalid_evidence",
        AiReviewProviderException => "ai_provider_error",
        GitHubApiException => "github_api_error",
        IOException => "io_error",
        _ => "unexpected_error",
    };

    private static void WriteError(string code, string message, bool json)
    {
        if (json)
        {
            string compactMessage = message.Length <= 400 ? message : $"{message[..397]}...";
            Console.Error.WriteLine(JsonSerializer.Serialize(
                new AgentErrorEnvelope(false, new AgentError(code, compactMessage)),
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

    internal static string ResolveAiProvider(OptionReader options)
    {
        string? explicitProvider = options.Optional("ai-provider")?.ToLowerInvariant();
        if (explicitProvider is not null)
        {
            _ = ResolveAiProviderProfile(explicitProvider);
            return explicitProvider;
        }

        string[] configured = AiProviderProfiles
            .Where(static profile => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(profile.KeyEnvironmentVariable)))
            .Select(static profile => profile.Name)
            .ToArray();
        if (configured.Length != 1)
        {
            throw new ArgumentException(
                configured.Length > 1
                    ? $"Multiple AI provider credentials are set ({string.Join(", ", configured)}); specify --ai-provider to choose the screenshot egress destination."
                    : "No AI provider credential was found; set ANTHROPIC_API_KEY, OPENAI_API_KEY, XAI_API_KEY, or GEMINI_API_KEY, or specify --ai-provider openai-compatible --ai-base-url http://127.0.0.1:PORT/v1/ --ai-no-auth true for a loopback provider.");
        }
        return configured[0];
    }

    internal static AiProviderProfile ResolveAiProviderProfile(string name) =>
        AiProviderProfiles.FirstOrDefault(profile => string.Equals(profile.Name, name, StringComparison.Ordinal)) ??
        throw new ArgumentException("--ai-provider must be anthropic, openai-compatible, grok, or gemini.");

    private static string ResolveAiKey(OptionReader options, string defaultVariable)
    {
        string variable = options.Optional("ai-key-environment-variable") ?? defaultVariable;
        string? key = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException($"Environment variable '{variable}' does not contain an AI provider credential.");
        }
        return key;
    }

    internal static Uri ResolveAiBaseUri(OptionReader options, AiProviderProfile profile, bool noAuth)
    {
        string? overrideValue = options.Optional("ai-base-url");
        string value = overrideValue ?? profile.BaseUrl;
        Uri uri = Uri.TryCreate(
            value.EndsWith("/", StringComparison.Ordinal) ? value : $"{value}/",
            UriKind.Absolute,
            out Uri? parsed)
            ? ValidateAiBaseUri(parsed)
            : throw new ArgumentException("--ai-base-url must be an absolute URL.");
        Uri defaultUri = ValidateAiBaseUri(new Uri(profile.BaseUrl, UriKind.Absolute));
        bool changesDestination = overrideValue is not null && !uri.Equals(defaultUri);
        bool explicitLoopbackNoAuth = noAuth && uri.IsLoopback;
        if (changesDestination &&
            !explicitLoopbackNoAuth &&
            !options.OptionalBool("ai-allow-custom-egress", false))
        {
            throw new ArgumentException(
                "A custom --ai-base-url changes where screenshots and provider credentials are sent; set --ai-allow-custom-egress true to acknowledge that destination.");
        }
        return uri;
    }

    private static Uri ValidateAiBaseUri(Uri baseUri)
    {
        if (!baseUri.IsAbsoluteUri || baseUri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException("--ai-base-url must be an absolute HTTP or HTTPS URL.");
        }
        if (baseUri.Scheme == "http" && !baseUri.IsLoopback)
        {
            throw new ArgumentException("Plain HTTP --ai-base-url values are allowed only for loopback hosts.");
        }
        return baseUri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseUri
            : new Uri($"{baseUri.AbsoluteUri}/", UriKind.Absolute);
    }

    private static string ReadPrompt(string? path)
    {
        if (path is null)
        {
            return AiReviewPrompts.Compare;
        }
        var file = new FileInfo(path);
        if (!file.Exists || file.Length is <= 0 or > 64 * 1024)
        {
            throw new ArgumentException("--prompt-file must exist and contain no more than 65536 bytes.");
        }
        string prompt = File.ReadAllText(file.FullName);
        if (string.IsNullOrWhiteSpace(prompt) || prompt.Contains('\0'))
        {
            throw new ArgumentException("--prompt-file must contain non-empty text without NUL characters.");
        }
        return prompt;
    }

    private static async Task WriteFileAtomicallyAsync(string path, byte[] bytes, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            throw new ArgumentException("The --output parent directory must exist.");
        }
        string temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            visual-evidence - durable before/after evidence for code review

            Commands:
              validate --evidence-root PATH [--expected-base SHA] [--expected-head SHA]
              validate --image PATH [--image PATH ...] | --image-root PATH
              review   --evidence-root PATH --output PATH --ai-model MODEL [--ai-provider NAME]
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
              --summary TEXT                       Required; maximum 2000 characters
              --assets-branch NAME                 Default: visual-evidence-assets
              --api-url URL                        Default: GITHUB_API_URL or api.github.com
              --comment-author-login LOGIN         Optional custom GitHub App bot login
              --token-environment-variable NAME    Default: GITHUB_TOKEN
              --publish-status true|false          Default: false
              --ai-review PATH                     Optional hash-bound ai-review-v1 JSON; comparison mode only

            AI review options:
              --task compare                       Default and only current task
              --ai-provider NAME                   anthropic, openai-compatible, grok, or gemini
              --ai-model MODEL                     Required; no moving model default
              --ai-base-url URL                    Optional provider endpoint override
              --ai-allow-custom-egress true|false  Required for a non-default credentialed endpoint
              --ai-key-environment-variable NAME  Optional credential environment variable override
              --ai-no-auth true|false              Loopback OpenAI-compatible providers only
              --ai-max-edge N                      Default: 1568; transport copy only
              --prompt-file PATH                   Optional prompt override; maximum 65536 bytes

            Validation limits:
              --maximum-image-bytes N              Default: 10485760
              --maximum-pixels N                   Default: 40000000
              --maximum-captures N                 Default: 50
              --allow-single-color true|false      Default: false
            """);
    }
}

internal sealed record AiProviderProfile(
    string Name,
    string KeyEnvironmentVariable,
    string BaseUrl,
    AiProviderProtocolKind Protocol,
    bool AllowsNoAuth);

internal enum AiProviderProtocolKind
{
    Anthropic,
    OpenAiCompatible,
    XaiResponses,
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
            bool flag = key is "json";
            if (!flag && index + 1 >= args.Length)
            {
                throw new ArgumentException($"Option '{name}' requires a value.");
            }
            if (!flag && args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{name}' requires a value; received option '{args[index + 1]}'.");
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

    public string RequiredLimited(string name, int maximumLength)
    {
        string value = Required(name);
        return value.Length <= maximumLength
            ? value
            : throw new ArgumentException($"--{name} must be no longer than {maximumLength} characters.");
    }

    public string? Optional(string name) => _values.TryGetValue(name, out IReadOnlyList<string>? values) ? values[^1] : null;

    public IReadOnlyList<string> All(string name) => _values.TryGetValue(name, out IReadOnlyList<string>? values) ? values : Array.Empty<string>();

    public void EnsureOnly(string command, IEnumerable<string> allowed)
    {
        var permitted = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);
        string? unknown = _values.Keys.FirstOrDefault(key => !permitted.Contains(key));
        if (unknown is not null)
        {
            throw new ArgumentException($"Unknown option '--{unknown}' for command '{command}'.");
        }
    }

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
