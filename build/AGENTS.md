# KeyLoad build infrastructure

## Purpose and entry points
- Own build infrastructure. Entry points: KeyLoad.AppHost/KeyLoad.AppHost.csproj and CodeQuality/FunctionalCompilationIdentity.targets.

## Boundaries and ownership
- Own Aspire composition and MSBuild identity capture, not runtime database implementations.
- Keep existing namespaces, diagnostics, persistence identities and behavior stable. Follow root AGENTS.md and project-local policies.

## Commands and verification
- Build: `dotnet build KeyLoad.slnx --no-restore --configuration Release`. Native tests and qualification retain their existing suite ownership.

## Skills
- Read applicable existing guidance; do not install skills or tools for structural moves.

## Protected risks
- Preserve unrelated working-tree changes, warning enforcement, source identities, real RF3 topology and honest qualification evidence.
