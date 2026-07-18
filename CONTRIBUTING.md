# Contributing

Contributions are welcome.

1. Open an issue for material behavior or manifest changes.
2. Keep capture adapters separate from publishing providers.
3. Add focused tests for security boundaries and cross-platform behavior.
4. Run `dotnet test VisualEvidence.slnx --configuration Release`.
5. Do not introduce provider-specific fields into the neutral manifest without discussing the portability impact.

## macOS SDK Note

Use Microsoft's official .NET SDK distribution or `dotnet-install.sh` when
building this repository on macOS. Homebrew's source-built SDK can provide
locally rebuilt AOT packs whose content hashes differ from the nuget.org packs
recorded in `packages.lock.json`.

If restore reports `NU1403` for `Microsoft.NET.ILLink.Tasks` or
`Microsoft.DotNet.ILCompiler`, switch to an official SDK and remove those two
package directories from `~/.nuget/packages` before restoring again. A failed
Homebrew restore can leave the incompatible packages in the shared cache.

## Released Action Canary

After changing the Action bootstrap or release packaging, run the trusted canary against an open pull request:

```console
gh workflow run native-action-canary.yml -f pull-request-number=123
```

The canary must download the released NativeAOT archive, verify its checksum and GitHub artifact attestation, publish the trusted PNG with the default `GITHUB_TOKEN`, and report one image plus an immutable asset commit. It must not set up .NET or compile the tool from source.

By contributing, you agree that your contribution is licensed under the MIT License.
