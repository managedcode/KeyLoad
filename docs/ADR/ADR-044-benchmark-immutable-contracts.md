# ADR-044: immutable comparative harness contracts

Status: Accepted implementation contract; source integration and qualification pending.
Owner: KeyLoad, authorized strict-analysis/resource repair goal.
Related: REQ-BC-020; ADR-033/034/035/041/043.

## Decision and caller boundary

The public collection properties and cached oracle returns use typed ImmutableArray,
following the established solution contract. The current caller API has no
alternate aliases.
Preserve every valid report JSON array/field/enum spelling and workload/wire value.
Required DTO missing/null/default arrays reject; optional null and present empty arrays
remain distinct. This malformed-input hardening defines the current report-input contract. SchemaVersion remains3; valid older array-bearing reports still read.

Rename the CLR zero topology member to Standalone, preserving Single JSON and
configuration spelling with JsonStringEnumMemberName and a TypeConverter. Numeric
values, binder case/trim/comma/numeric behavior and validation remain exact; new CLR
spelling is accepted additively. Replace ComparisonFailure everywhere with standard
ComparisonFailureException constructors; safe report text still shows coded message
only, never inner exception secrets. No enum or exception shim.

```mermaid
flowchart LR
    Config[Existing Single configuration] --> Topology[Standalone CLR member]
    Corpus[Owned deterministic corpus] --> Immutable[Immutable vectors and cached oracles]
    Engines[Official engine clients] --> Validation[Mutable attempt validation]
    Immutable --> Engines
    Validation --> Report[Freeze final samples once]
    Report --> Json[Unchanged valid schema3 JSON arrays]
    Json --> Readers[Existing report and site readers]
```

## Implementation contract

Verification order: author real serializer/configuration/corpus/exception fixture inputs first; then the shared contract owner (Contracts.cs, BenchmarkDataset.cs and topology/contract-json/exception helpers under Features/BenchmarkComparisons/, namespace/assembly KeyLoad.Comparisons); then the disjoint engine consumers and the shared runner/report join; exact diff/build/formatter review; actual delivered-SHA GitHub full suites and honest retained evidence. No local tests/load. Architecture and contracts do not change beyond this decision; unsupported actual APIs, upstream defects or overlap are escalated.

Exclusively owned arrays may attach with ImmutableCollectionsMarshal.AsImmutableArray
only when all mutable aliases retire. Caller-owned input freezes once. Build corpus
and oracle arrays once; cache hits reuse immutable storage. Scalar cosine keeps the
exact original summation, tie breaking and hash sequence. Mutable samples remain
mutable through all post-timing validation, then attach once; no report/writer copy.
KeyLoad vector callers use existing immutable SDK fields directly. OpenSearch,
Qdrant and Postgres numeric transport serializes the same values without ToArray.

Report JSON keeps its existing Web options, indentation and enum converter. Register
the established strict immutable converters via public JsonDefaults.Options.GetConverter
for the exact concrete element types; do not clone product JSON defaults wholesale,
duplicate the converter or internalize the harness API. Required array properties
carry JsonRequired so missing input rejects. Constructor-owned getter-only corpus
properties retain their original input boundary; corpus generation is not a DTO
deserialization contract and those properties receive no required setter metadata.
Public documentation follows real
ownership, null/default behavior and code; no hidden severity/suppression changes.

The public enum's source-owned TypeConverter is a documented public configuration
extension. Its attribute and external TypeDescriptor fixtures establish the actual
reflection path; no artificial instance, global provider registration, IVT or
suppression is introduced to evade CA1812. Its ConvertFrom parameter follows the
base EnumConverter object contract; valid/malformed input behavior is unchanged.
This deliberate configuration surface joins the existing external
binder/JSON/TypeDescriptor fixtures. See Microsoft's [CA1812 constructor-call
analysis](https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/quality-rules/ca1812)
and [TypeConverter attribute/reflection contract](https://learn.microsoft.com/en-us/dotnet/api/system.componentmodel.typeconverter?view=net-10.0).

PostgreSQL DDL/static SQL redesign, registration/topology/receipt changes, generated
site data and numeric gates are separate contracts. Every existing real engine/
cluster/.NET/MCP/recovery gate stays mandatory. Fake fixtures and the existing six-engine assertion set cannot qualify this stage.
Historical six-engine evidence remains separate from the current required gates.

This is a source-only harness contract; product persistence/API/security are
unchanged. Keep all current callers and fixture ownership aligned with these
signatures. Rollback this exact contract/caller/fixture unit together, without
alternate APIs. Keep this ADR Accepted
until implementation, all mapped tests and delivered-SHA evidence actually exist.
