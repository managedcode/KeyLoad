# Three pipelines and dated release delivery

Owner correction 2026-10-03 explicitly replaces the five-workflow layout with CI,
Benchmarks and Release. CI combines build and every ordinary project test;
Benchmarks runs all load/comparison suites and publishes their freshly aggregated
metrics; Release builds real database distributions/images/packages and publishes
an immutable dated GitHub release/tag.

Keep the existing source/workload/native topology, isolated Linux jobs, exact
artifact validation and complete site/browser/coverage gates. Moving jobs without
changing executor guards or exact current producer selection would fail or publish
another run's results. Preserve authentic legacy KeyLoad CI archives and identify
new current CI pushes as authenticated nonproducer entries.

Recommended: three top-level YAML files; move complete Tests jobs into CI and
Website qualify/deploy jobs behind Benchmarks aggregate/image success. Bind site
publication directly to trusted current run/attempt/SHA; retain historical-input
validation separately. Manual Release on main, a serialized UTC version reservation
artifact, read-only build/pack/publish/image export, and write-limited final delivery.
Do not expose a successful release before its build/source qualification succeeds.

Version: configured major/minor, UTC yyMMdd and daily sequence starting at 1.
Use the same four-component package/tag version; supply bounded stable CLR assembly
and file versions because the date exceeds CLR metadata's 16-bit component limit.
Retry the same run's reservation; never overwrite another tag/image/asset. Capture
real hashes, digests, CI proof and source identity. Failed existing runtime/native
checks remain real blockers; do not weaken them to demonstrate publication.
