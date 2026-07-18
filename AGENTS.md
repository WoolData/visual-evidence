# Agent Guide

Visual Evidence publishes existing PNG files into pull-request comments. It does not capture screenshots.

Start with `visual-evidence describe --json`; do not read the full README for routine use.

Choose one input mode:

- `--image path.png` (repeatable): selected images.
- `--image-root captures`: every PNG below a folder.
- `--evidence-root evidence`: manifest-backed before/after comparison.

Use `doctor --json` before the first remote operation, `publish --json` to publish, and `verify --json` after publication. Never pass tokens on the command line; set `GITHUB_TOKEN`. Treat nonzero exit codes and `{"ok":false}` as failures.

Capture adapters belong outside this repository's publishing core.
