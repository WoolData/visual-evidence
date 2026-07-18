---
name: visual-evidence
description: Publish existing PNG screenshots into a GitHub pull request as durable, exact-revision visual evidence. Use for one image, an image folder, or a manifest-backed before/after matrix; this tool does not capture screenshots.
---

# Publish Visual Evidence

1. Run `visual-evidence describe --json` once to discover the installed protocol.
2. Set `GITHUB_TOKEN`; never place the token in arguments or logs.
3. Check the target with `visual-evidence doctor --repository OWNER/REPO --change-number N --json`.
4. Publish exactly one mode:

```text
visual-evidence publish --repository OWNER/REPO --change-number N --image shot.png --summary "Visible change" --json
visual-evidence publish --repository OWNER/REPO --change-number N --image-root captures --summary "Visible change" --json
visual-evidence publish --repository OWNER/REPO --change-number N --evidence-root evidence --summary "Before and after" --json
```

`--image` is repeatable. Use the manifest-backed mode when before/after provenance and capture-environment compatibility matter.

For optional advisory comparison narration, validate first, set exactly one of
`ANTHROPIC_API_KEY`, `OPENAI_API_KEY`, `XAI_API_KEY`, or `GEMINI_API_KEY`, then run:

```text
visual-evidence review --evidence-root evidence --output ai-review-v1.json --ai-model MODEL --json
```

If multiple keys exist, add `--ai-provider anthropic|openai-compatible|grok|gemini`;
never let an agent guess the screenshot egress destination. A credentialed
custom endpoint also requires `--ai-allow-custom-egress true`. AI review is
advisory and publication must still work when it is skipped or fails.

5. Run `visual-evidence verify --repository OWNER/REPO --change-number N --json`.

Stop on a nonzero exit code or `"ok":false`. Report the compact JSON result; do not paste normal command logs.
Re-running `publish` is safe: it updates this publisher's marked comment and avoids empty evidence commits.
