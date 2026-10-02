# Embedded benchmark source boundary

The strict full solution cut has14 findings in the existing BenchmarkDotNet0.15.8
executable. Its public fixture conflicts with CA1515 for applications, and Program
owns benchmark behavior, raw clock access and an undisposed store contract. Making
the fixture internal would break the actual generated external consumer.

Pinned primary source confirms the generated public runner inherits the fixture:
[template](https://raw.githubusercontent.com/dotnet/BenchmarkDotNet/v0.15.8/src/BenchmarkDotNet/Templates/BenchmarkType.txt).
The converter also requires public methods. Reflection discovery alone cannot
prove generated consumer compilation or execution. In-process/default toolchain
changes, diagnostic suppression, metadata spoofing or deleting operations are
rejected: they change behavior or hide the contract rather than satisfy it.

Choose a dedicated nonpackable KeyLoad.BenchmarkScenarios library with the public
unsealed fixture in Features/BenchmarkComparisons. The existing KeyLoad.Benchmarks
executable owns only the typed runner and original BenchmarkSwitcher invocation,
pointing at the fixture assembly. This keeps the three existing operations/inputs,
MemoryDiagnoser, command-line defaults and real ZoneTree/engine behavior. Public
fixture inheritance is explicitly required by upstream; no product database API,
server topology, corpus/report/site or dependency release changes.

Give the fixture a proper disposable lifetime, safe uninitialized/repeated cleanup
and cleanup of resources acquired before setup failure. Preserve seeding and the
real clock through TimeProvider.System. Cohesive helpers satisfy400/200/50/3 and
do not add extra payload copies or change the timed operation itself.

First author real TUnit metadata and setup/read/cleanup assertions. A TUnit case
invokes the actual executable with Dry/full-JSON exporter, verifies all three
methods have real successful measurements, and retains raw output/reports only
in CI. This proves generated runner qualification, not latency, RF3 or durability.
Environmental partial-setup fault cleanup additionally requires complete manual
ownership/error-path source review; no fake store or fault-injection seam is added.

One economical worker owns the tightly coupled fixture/host/new tests, tests first;
lead owns policy, ADR, solution/project references, inventory, CI/artifact and final
review. Query/storage test workers remain disjoint. Current native/MCP migration
and Unit225 prerequisites prevent complete qualification; keep them explicit.

Observed graph join: the first actual25-project restore fails NU1608 only in
UnitTests. BenchmarkDotNet0.15.8 brings CSharp4.14.0 with exact Common4.14.0,
while the real Orleans metadata graph already owns Common5.0.0. The existing
central Common pin must remain; hiding NU1608 or excluding benchmark runtime
compiler assets would leave an invalid generated-consumer graph. Choose a central
CSharp5.0.0 peer pin matching Common5.0.0. The actual cached5.0.0 nuspec requires
Common exactly5.0.0 and satisfies BenchmarkDotNet's minimum4.14.0. Keep SDK Roslyn
asset selection, Orleans, BenchmarkDotNet and all ManagedCode versions unchanged;
restore, enabled build and real generated Dry CI remain required compatibility proof.
