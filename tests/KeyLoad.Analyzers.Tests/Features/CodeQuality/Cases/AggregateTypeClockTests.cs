using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-TIME-001: real compilation evaluates migration expiry with an injected clock.</summary>
internal sealed class AggregateTypeClockTests
{
    private const string AssemblyName = "KeyLoad.Core";
    private const string SourcePath = "/repo/src/KeyLoad.Core/DatabaseEngine.cs";
    private const string TypeName = "DatabaseEngine";
    private const int MemberCount = 210;

    [Test]
    public async Task InjectedUtcCrossesMigrationDeadlineInTheActualCompilerOperation()
    {
        const string header = "namespace KeyLoad.Core;\ninternal sealed class DatabaseEngine\n{\n";
        const string footer = "\n}";
        var members = Enumerable.Range(0, MemberCount).Select(static index => $"    private int Value{index};");
        var source = header + string.Join('\n', members) + footer;
        var clock = new MigrationClock(new(2026, 10, 31, 23, 59, 59, TimeSpan.Zero));
        var analyzer = new AggregateTypeCodeLineCountAnalyzer(clock);
        var allowed = await NumericAnalyzerFixture.AnalyzeSourcesAsync(analyzer, AssemblyName, (SourcePath, source));
        await Assert.That(allowed).IsEmpty();
        clock.Advance(TimeSpan.FromSeconds(1));
        var expired = await NumericAnalyzerFixture.AnalyzeSourcesAsync(analyzer, AssemblyName, (SourcePath, source));
        await NumericAnalyzerFixture.AssertSingleFindingAsync(expired, "KLD0031", DiagnosticSeverity.Error, SourcePath, line: 1);
        await Assert.That(expired[0].GetMessage(System.Globalization.CultureInfo.InvariantCulture)).Contains(TypeName);
    }

    private sealed class MigrationClock(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
        internal void Advance(TimeSpan elapsed) => utcNow += elapsed;
    }
}
