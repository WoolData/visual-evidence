# Visual Evidence

Durable, exact-revision before/after screenshots for code review, without a browser, clipboard, or file picker.

Visual Evidence validates screenshots produced by any capture tool, stores normalized PNGs as ordinary Git objects on a protected append-only branch, and renders them inline in a pull-request comment using links pinned to the evidence commit.

```text
capture adapter -> evidence manifests -> validator -> asset store -> review provider
 WPF / Avalonia       neutral JSON       SkiaSharp      Git branch      GitHub PR
 Playwright / PNG                                                       GitLab MR (planned)
```

Capture and publication are deliberately separate. The trusted publisher never executes capture code and does not care whether an image came from WPF, Avalonia, Playwright, Appium, or another PNG producer.

## Why

GitHub's web UI can upload images to its attachment service, but there is no clean documented API for automation to perform that same comment-attachment upload. Browser automation can operate the picker, but it is slow, focus-sensitive, and difficult to run reliably without a desktop session.

Visual Evidence uses GitHub's supported Git Database and comments APIs instead:

1. Validate the exact pull-request head and merge base.
2. Decode and normalize every PNG with SkiaSharp.
3. Create Git blobs, a tree, and an append-only asset commit.
4. Update the protected assets branch without force.
5. Post or update one structured pull-request comment.
6. Verify that the comment still matches the current head and merge base.

The result is not merely a screenshot. It is review evidence with explicit provenance and an exportable chain of custody.

## Quick Start

Already have screenshots and just need them in a PR? Publish one image, selected images, or a folder:

```powershell
visual-evidence publish --repository owner/repo --change-number 123 `
  --image screenshots/step-1.png --image screenshots/step-2.png `
  --summary "Current Step 1 and Step 2 layouts" --json

visual-evidence publish --repository owner/repo --change-number 123 `
  --image-root screenshots --summary "Current UI" --json
```

The filenames become reviewer-facing labels. Every PNG receives the same validation, immutable asset commit, exact head/base markers, and idempotent PR comment as a comparison matrix. Screenshot capture remains a separate concern.

### 1. Produce evidence

Create the same capture matrix at the merge base and pull-request head:

```text
evidence/
  before/
    manifest.json
    captures/home-dark-small.png
  after/
    manifest.json
    captures/home-dark-small.png
```

Both manifests use the [v1 evidence schema](schema/evidence-manifest-v1.schema.json). Capture paths are relative to their snapshot directory. Capture keys must match between `before` and `after`.

The environment block records the operating system, architecture, runner image, adapter, renderer, scale, and font set. Before and after must have the same calculated compatibility key; evidence from different rendering environments is rejected.

See [the example manifest](examples/after-manifest.json).

Capture tools may write manifests directly or let the CLI inventory a PNG directory:

```powershell
dotnet run --project src/WoolData.VisualEvidence.Cli -- manifest `
  --snapshot after --revision $env:GITHUB_SHA `
  --capture-root ./evidence/after/captures `
  --output ./evidence/after/manifest.json `
  --os windows --architecture x64 --runner-image windows-2025 `
  --capture-adapter playwright --adapter-version 1.58.0 `
  --renderer chromium --render-scale 1 --font-set-hash bundled-fonts-v1
```

### 2. Protect the assets branch

Create a repository ruleset targeting `refs/heads/visual-evidence-assets` with these rules:

- block deletion;
- block non-fast-forward updates;
- no bypass actors.

The publisher performs only fast-forward updates. This protection is load-bearing: SHA-pinned links are durable while their commits remain reachable from the append-only branch.

### 3. Publish from GitHub Actions

```yaml
name: Publish visual evidence

on:
  pull_request:
    types: [opened, synchronize, reopened, edited]

permissions:
  contents: write
  pull-requests: write

jobs:
  evidence:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v6
      - name: Capture before and after
        run: ./your-capture-command --output evidence
      - uses: WoolData/visual-evidence@v0.2.1
        with:
          evidence-root: evidence
          summary: Keeps primary actions visible at compact window sizes.
          github-token: ${{ secrets.GITHUB_TOKEN }}
```

For standalone screenshots, replace `evidence-root` with `image-root`, or pass newline-delimited paths through `images`.

Pin the Action to a full commit SHA in security-sensitive repositories. The tag above keeps the introductory example readable.

Normal Action invocations download the platform-specific NativeAOT archive declared in [`tool-manifest.json`](tool-manifest.json), verify its SHA-256 and GitHub artifact attestation, and run it directly from the runner's temporary tool directory. Windows x64, Linux x64, and both Apple Silicon and Intel macOS are supported. The Action does not require .NET or compile repository source for consumers. Contributors testing an unreleased source change may opt in with `use-source: true`.

The repository's manually dispatched `Native Action Canary` workflow exercises this exact consumer path with the default `GITHUB_TOKEN`. It publishes a real PNG through the released Action and asserts the reported mode and capture count without compiling the tool from source.

To publish the optional `visual-evidence/published` commit status, grant `statuses: write` and set `publish-status: true`.

The default `GITHUB_TOKEN` and user tokens are both supported. When using a custom GitHub App installation token, set `comment-author-login` to that App's bot login, such as `my-app[bot]`, so repeated runs update only the App's own evidence comment.

The CLI is NativeAOT-compatible. CI publishes and executes native binaries on Windows, macOS, and Linux; release automation can distribute those binaries without requiring a .NET runtime or SDK on the consumer machine.

### 4. Use the CLI

Agents should begin with `visual-evidence describe --json`. The compact protocol avoids README discovery during routine runs; an installable skill is available at [`skills/visual-evidence/SKILL.md`](skills/visual-evidence/SKILL.md).

```powershell
dotnet run --project src/WoolData.VisualEvidence.Cli -- validate `
  --evidence-root ./evidence

$env:GITHUB_TOKEN = "..."
dotnet run --project src/WoolData.VisualEvidence.Cli -- publish `
  --repository owner/repository `
  --change-number 123 `
  --evidence-root ./evidence `
  --summary "Keeps primary actions visible at compact sizes."

dotnet run --project src/WoolData.VisualEvidence.Cli -- verify `
  --repository owner/repository `
  --change-number 123
```

Tokens are read only from the named environment variable and are never accepted as command-line values.

## Manifest Contract

Each snapshot manifest contains:

```json
{
  "schemaVersion": 1,
  "snapshot": "after",
  "revision": "<full-git-object-id>",
  "environment": {
    "os": "macos",
    "architecture": "arm64",
    "runnerImage": "macos-15-arm64",
    "captureAdapter": "avalonia-headless",
    "adapterVersion": "1.0.0",
    "renderer": "skia",
    "renderScale": 1,
    "fontSetHash": "<capture-defined-font-fingerprint>",
    "compatibilityKey": "<sha256-of-canonical-environment>"
  },
  "captures": [
    {
      "key": "home-dark-small",
      "label": "Home, dark theme, small window",
      "path": "captures/home-dark-small.png",
      "width": 1000,
      "height": 700,
      "sha256": "<sha256-of-source-png>"
    }
  ]
}
```

The compatibility key is SHA-256 over these lower-cased, trimmed fields joined by line feeds in order: OS, architecture, runner image, capture adapter, adapter version, renderer, invariant round-trip render scale, and font-set hash.

The CLI can calculate it for capture adapters:

```powershell
dotnet run --project src/WoolData.VisualEvidence.Cli -- environment-key `
  --os macos --architecture arm64 --runner-image macos-15-arm64 `
  --capture-adapter avalonia-headless --adapter-version 1.0.0 `
  --renderer skia --render-scale 1 --font-set-hash bundled-fonts-v1
```

## Validation

The publisher treats manifests and images as untrusted input. It enforces:

- exact head and merge-base revisions;
- matching unique before/after capture keys;
- same capture-environment compatibility key;
- relative, contained paths with no symbolic links or reparse points;
- PNG-only input with byte and decoded-pixel limits;
- declared SHA-256 and dimensions matching the decoded image;
- rejection of undecodable and single-color frames by default;
- decode and re-encode before publication, discarding untrusted PNG metadata;
- strict asset paths and non-force branch updates;
- idempotent updates to the publisher's existing marker comment.

## Is The Screenshot In The Pull Request?

It renders inline in the pull-request conversation, but the PNG bytes are not native PR attachments. They are Git objects retained by the protected assets branch, and the comment contains URLs pinned to the evidence commit.

```text
pull-request comment -> pinned repository URL -> asset commit -> normalized PNG
```

| Property | GitHub web attachment | Evidence commit |
|---|---|---|
| Uploadable by automation | No (the upload flow is browser-only) | Yes |
| Enumerable through an API | No | Yes (ordinary Git refs and trees) |
| Exportable and backupable | No | Yes (clones and mirrors with repository refs) |
| Provenance bound to head and merge-base revisions | None | Exact |
| Survives repository migration off GitHub | No (attachment URLs still point to GitHub) | Yes |
| Survives assets-branch deletion or force-push | Yes (not stored in Git) | Only while the evidence commit remains reachable |
| Retention guarantee | Undocumented (observed persistence, no contract) | Repository owner defines it |

The models fail in opposite directions: web attachments are convenient but externally managed and cannot leave GitHub; evidence commits are owned, auditable, and portable, but demand one discipline in exchange — the assets branch must stay append-only and protected so every evidence commit remains reachable.

## Platform Support

The validation and publication core runs on Windows, macOS, and Linux.

| Capture adapter | Windows | macOS | Linux |
|---|---:|---:|---:|
| WPF `RenderTargetBitmap` | Yes | No | No |
| Avalonia headless Skia | Yes | Yes | Yes |
| Playwright | Yes | Yes | Yes |
| Arbitrary PNG producer | Yes | Yes | Yes |

Do not compare captures produced by different environment compatibility keys. Fonts and rasterization can differ even when application code is identical.

## Providers

The core exposes separate ports for:

- resolving a change request and its revisions;
- storing evidence assets;
- publishing review comments;
- publishing commit status.

V0.1 includes a GitHub provider. A GitLab provider can implement merge-request resolution, repository commits, notes, and commit statuses without changing the evidence manifest or validator.

## Fork Security

Never run untrusted pull-request code with a privileged `pull_request_target` token.

For public forks, separate the workflow:

1. An unprivileged `pull_request` job checks out and executes the proposed code, captures images, and uploads an Actions artifact.
2. A trusted publisher downloads only that artifact, treats every file as untrusted data, validates and normalizes it, and publishes without executing anything from it.

See [SECURITY.md](SECURITY.md) for the trust boundary.

## Current Scope

V0.1 intentionally focuses on trustworthy publication rather than capture:

- GitHub pull requests;
- protected same-repository asset branch;
- exact-revision validation;
- idempotent Markdown review comment;
- optional commit status;
- cross-platform CLI and Action.

Potential follow-ups include a GitLab provider, optional pixel-diff images, separate asset repositories, retention reporting, and reusable capture adapters.

## License

[MIT](LICENSE), copyright Wool Data Inc.
