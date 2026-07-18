# Distribution

Visual Evidence ships immutable NativeAOT archives and a .NET tool package from
GitHub Releases. Public registry publication is deliberately a separate,
human-approved step.

## NuGet.org

One-time setup:

1. A Wool Data package owner creates or confirms the NuGet.org account.
2. Create a scoped push API key for `WoolData.VisualEvidence.Tool`.
3. Create the `nuget-org` GitHub environment, add `NUGET_API_KEY`, and configure
   required reviewers if desired.
4. Run `Publish NuGet Package` with an existing release version such as `0.3.0`.

The workflow downloads the package from the immutable GitHub release, verifies
its SHA-256 and GitHub artifact attestation, and then publishes it. The API key
is provided through the `NUGET_API_KEY` environment variable and never appears
in process arguments.

The first successful push claims the package ID. Later pushes use the same
workflow and are safe to retry because duplicate versions are skipped.

## GitHub Marketplace

Marketplace publication requires an organization owner in GitHub's release UI:

1. Accept the GitHub Marketplace Developer Agreement for WoolData if prompted.
2. Edit the current stable release and select **Publish this Action to the
   GitHub Marketplace**.
3. Confirm the unique `Visual Evidence` name and choose the most relevant
   continuous-integration/code-quality categories offered by the UI.
4. Publish, then verify the listing installs `WoolData/visual-evidence@v0.3.0`.

GitHub requires this human flow, including two-factor authentication. Do not
automate it with browser credentials.
