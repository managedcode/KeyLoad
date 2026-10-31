# QualifySite

## Purpose, entry points and ownership

- Owns static landing-page generation from original successful benchmark data. Entry: action.yml. This action has no relationship to product execution or its qualification.
- Never add .NET/AppHost builds, Aspire, containers, TUnit/browser/analyzer suites, coverage or product checks to this action or its publication dependencies. Only benchmark-data validation, static generation and artifact upload belong here. Specific owner approval is required to change this boundary.
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
