# DeploySite

## Purpose, entry points and ownership

- Owns the BenchmarkComparisons DeploySite composite action under ADR-064/AC-PIPE-002..003. Read root/workflow policy, ReleaseDelivery and BenchmarkComparisons specs before edits. Entry: action.yml.
- Publish the generated static landing page after benchmark-data validation and its Node build. No .NET, Aspire, AppHost, containers, TUnit/browser/analyzer suites, coverage or product qualification belongs in the Website dependency graph. Adding such coupling requires specific owner approval.
- The qualification compares both new actions' source/policy with trusted control and the current website checkout. Deployment writes remain confined to the needs-gated deploy job.

## Boundaries and protected risks

Follow the [root policy](../../../../../AGENTS.md) and the owning feature specification. Keep changes in their actual owner, preserve unrelated work, and remove replaced code together with exclusive callers and fixtures. Use Orleans calls for distributed execution and silo-local ZoneTree owners for files. Preserve persisted authorization, operation correctness, resource bounds, cancellation and joined shutdown. Never turn a static check or focused test into a claim of complete runtime qualification.

## Commands and verification

From the repository root:

```sh
actionlint .github/workflows/website.yml
```

For this static publication action, use YAML/JavaScript syntax checks and the actual Node build. Product builds and test qualification are independent. Public figures retain original successful GitHub Benchmarks evidence.

## Skills

Use the installed Orleans skill for Orleans design and the relevant installed formatting, Aspire or diagnostics skill when the task needs it. Follow explicit owner scope and dependency-adoption rules.
