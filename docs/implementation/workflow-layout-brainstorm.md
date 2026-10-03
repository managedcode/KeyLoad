# Workflow layout

The owner requires five plainly named pipelines: CI, Tests, Benchmarks, Release and Website.
Repository-rule checks always belong to CI. All comparison tests, including the
native TimeSeries image test, belong to one separate Benchmarks workflow.

Scope: workflow composition, exact workflow/source provenance contracts, their
regressions and delivery docs. Runtime database, native topologies, measurements,
website design and unrelated working-tree changes are outside this task.

Move the existing isolated native comparison jobs unchanged into benchmarks.yml,
with their own full Release build/format/rules prerequisite. CI owns PR checks; Tests owns ordinary unit,
scalar, analyzer, process recovery and Docker/Aspire RF3 qualification in tests.yml.
Release builds actual NuGet packages without adding publication credentials.
Keep each comparison cell on its own Linux runner. Pages follows Benchmarks.

Risks: producer names/paths are checked throughout artifact authentication;
historical CI-produced measurement archives must remain verifiable as historical
inputs without being relabelled or accepting arbitrary producer identities.
The current main full workflows already fail; inspect exact baseline failures,
then distinguish new layout qualification from existing product failures.

Parallel work: read-only provenance audit can start immediately; workflow/policy/
shared contract ownership stays with the lead. Bounded disjoint regression and
adapter updates start only after acceptance and ADR contracts are written.
