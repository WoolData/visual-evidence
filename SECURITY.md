# Security

## Reporting

Please report vulnerabilities through GitHub private vulnerability reporting for this repository. Do not open a public issue for a suspected vulnerability.

## Trust Boundary

Capture code and generated images can originate from an untrusted pull request. A privileged publication job must treat the evidence directory as untrusted data:

- never execute files from the evidence artifact;
- validate manifest paths and reject links or path traversal;
- enforce byte and decoded-pixel limits;
- decode and normalize PNG data before publication;
- verify the exact pull-request head and merge base;
- grant only `contents: write` and pull-request comment permissions;
- do not check out or execute pull-request code from a privileged `pull_request_target` or `workflow_run` job.

For forked pull requests, use an unprivileged capture workflow and a separate trusted publisher that downloads only the evidence artifact, validates it, and never executes its contents.

## Advisory AI Review

Screenshots are untrusted multimodal input. Text rendered inside an image can
contain prompt-injection instructions. AI review providers must treat that text
as content under review, never as instructions, and their output must remain
advisory. Do not use AI visual findings as an approval or merge gate.

Run review before privileged publication. The publisher does not contact model
providers; it accepts only strict `ai-review-v1` JSON whose source hashes match
the already validated evidence. It stores that review with the images in one
asset commit and renders only a bounded, escaped digest. Provider credentials
must remain in environment variables and must never enter command arguments,
manifests, review JSON, comments, or asset paths.

## Asset Reachability

Evidence links remain durable only while their asset commits remain reachable. Protect the configured assets branch against deletion and non-fast-forward updates. Do not add bypass actors.
