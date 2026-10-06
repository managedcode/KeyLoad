# ReleaseDelivery

## License distribution

The owner's final decision on 2026-10-05 selects Elastic License 2.0 to permit
application use while reserving third-party hosted/managed database services
for separate authorization from ManagedCode, without automatic expiry.
[ADR-107](../ADR/ADR-107-elastic-license.md) owns the license and
implementation contract; the complete root [LICENSE](../../LICENSE) is authoritative.

| Requirement | Acceptance and pass/fail evidence |
| --- | --- |
| REQ-LIC-001 standard license | AC-LIC-001: the complete ELv2 body matches the authoritative published source byte-for-byte, with a separate KeyLoad/ManagedCode copyright and licensor header. The hosted/managed-service, license-key and notice-preservation limitations remain unchanged; no BSL Change Date, Apache conversion or GPL covenant remains. Exact standard-text comparison is the manual legal-text exception; absent or altered standard terms fail. |
| REQ-LIC-002 accurate distribution metadata | AC-LIC-002: NuGet packs the actual root LICENSE as a file and dotnet publish copies it into server/CLI distribution roots; README identifies ELv2/source availability and site JSON-LD links to that license. Inspect an actual local package/publish output and built/source metadata; existing SiteMetadataTests retain the full actual-builder flow. An obsolete MIT/BSL declaration or missing license file fails. |
| REQ-LIC-003 preserve independent licenses | AC-LIC-003: third-party license notices and vendored Three.js bytes remain unchanged. Review the scoped diff and original vendor manifest hashes; no dependency relicensing or runtime/storage changes are permitted. |

TASK-LIC-001 freezes the contract, TASK-LIC-002 updates the license/metadata and
existing site assertions, and TASK-LIC-003 checks exact text, package output,
governance and the final canonical solution build. Runtime RF3, coverage and
performance gates remain unchanged; license metadata does not qualify the database.

```mermaid
flowchart LR
    License[Root Elastic License 2.0 LICENSE] --> Package[NuGet license file]
    License --> Readme[Product licensing summary]
    License --> Site[Source available metadata]
    License --> Commercial[Separate authorization for hosted database services]
```

The owner requires four workflows (Build and Tests, Benchmarks, Website and Release)
and a real dated database release. ADR-112 owns the separate Website boundary. The immutable
version/tag is `v<major>.<minor>.<yyMMdd>.<daily-build>`; major/minor come from central
source configuration, UTC defines the date, and the positive daily sequence is
frozen to the authenticated release run. Example: `v0.1.261003.1` for the current
0.1 base. NuGet supports four numeric components; CLR assembly/file components
have smaller bounds, so use stable `M.m.0.0` / `M.m.0.N` and full informational
version ([NuGet](https://learn.microsoft.com/en-us/nuget/concepts/package-versioning),
[CLR metadata](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.assemblyversionattribute)).
NuGet packages retain the current source's development stage as `M.m.yyMMdd.N-dev`;
tag, image and informational versions retain the requested four numeric components.
This keeps the alpha-only Cartograph dependency visible and respects
[NU5104](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu5104)
without suppressing diagnostics or claiming stable packages.

The owner subsequently deferred packaging because the product is unfinished.
Release remains manual and prepared for a later explicit release/readiness request;
current workflow implementation and CI checks do not authorize dispatch or publication.

Requirements/acceptance are REQ/AC-PIPE-001..004 and REQ/AC-REL-001..003 in the
[acceptance matrix](../implementation/pipeline-release-v2-acceptance.md).
[ADR-064](../ADR/ADR-064-three-pipeline-release-delivery.md) is required for release
permissions, version identity, deployment artifacts and current-producer handoff.

```mermaid
flowchart LR
    PR[PR or main source] --> CI[Build and Tests solution rules tests RF3]
    Main[Own main push or manual] --> Benchmarks[All native load comparisons]
    Benchmarks --> Aggregate[Complete authenticated JSON aggregate]
    WebSource[Trusted main source or manual] --> Website[Separate Website workflow]
    Aggregate --> Trigger[Dispatch Website only]
    Trigger --> Website
    Website --> Qualify[Applicable site tests browser coverage]
    Qualify --> Deploy[Publish with ready metrics or without figures]
    Manual[Manual own-main Release] --> Version[UTC dated reservation]
    Version --> Build[Full build packages database images]
    CI --> SourceGate[Successful exact-source Build and Tests proof]
    Build --> Publish[Versioned GHCR assets tag GitHub Release]
    SourceGate --> Publish
```

Canonical ownership:
- shared infrastructure: `.github/workflows/ci.yml`, `benchmarks.yml`, `website.yml`, `release.yml`,
  root Dockerfile, the existing comparison Dockerfile and `Directory.Build.props`;
- tooling: `scripts/Features/ReleaseDelivery/` version/asset helpers and distribution;
- focused contracts/tests: `tests/KeyLoad.UnitTests/Features/ReleaseDelivery/`;
- source/provenance handoff: existing BenchmarkComparisons production adapters and
  their TUnit suites, preserving their existing canonical slice;
- frontend/public API/storage/schema: N/A; this task changes delivery and consumes
  existing measurements without changing database or webpage behavior.

Build artifacts contain all actual packable projects, a self-contained Linux x64
server distribution, RF3 persistent-container configuration, and version-labelled
images built from the existing owned Dockerfiles. Database credentials are runtime
configuration and are never bundled into source/assets. Existing RF3 membership,
Orleans routing and node-local ownership are retained; distribution packaging does
not establish production readiness, endurance or power-loss durability.

Reservation is source/run/date bound and retained before expensive work. Its daily
sequence is at least the actual daily release-run ordinal and exceeds already
published dated tag counters. A retry validates and reuses its reservation; it
cannot silently change version/source or steal another run's tag/assets. Publication
checks successful latest exact-source CI and all owned hashes, then creates/reuses only
its own immutable tag/release/image identities. Failed source tests, conflicts,
missing credentials or incomplete artifacts fail without force or overwrite.

No NuGet feed publication or version-bump commit is included. The requested NuGet
files are real GitHub Release assets. Site publication authenticates the current
benchmark run/attempt/SHA, independently preserves the required genuine historical
archive, and retains every existing test/browser/coverage/freshness gate.
