# KeyLoad.Analyzers

## Purpose and entry points
- Own the source-editable compiler rules in `Features/CodeQuality/`; project entry is `KeyLoad.Analyzers.csproj`. Feature contract: `docs/Features/CodeQuality.md`; implementation decision: ADR-033.

## Boundaries and ownership
- Compile against Roslyn shipped by the pinned .NET SDK. Attach centrally as an analyzer, never as a runtime product dependency. All rule behavior belongs to the CodeQuality slice.
- Preserve imported semantics while adapting KeyLoad assembly/method ownership. No sibling reference, fake framework symbols, silent severity reduction or runtime contract migration.
- Generated code is excluded using Roslyn's official generated-code API. Keep semantic analysis cancellable and concurrent.

## Commands and verification
- Development build: `dotnet build src/KeyLoad.Analyzers/KeyLoad.Analyzers.csproj --no-restore --configuration Release`; integrated development gate is the root solution build.
- Qualification and TUnit execution occur only in GitHub Actions; retain exact run/SHA and reports. No local test, recovery or load execution.

## Applicable skills
- No repository skills installed. Do not install tools/skills or modify global configuration.

## Protected risks
- Keep diagnostic IDs stable and unique. Compiler references must use the pinned SDK, with no private dependency payload attached to consumer runtime outputs. Preserve all unrelated dirty checkout changes.
