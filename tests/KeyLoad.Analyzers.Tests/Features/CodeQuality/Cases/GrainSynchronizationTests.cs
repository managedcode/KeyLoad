namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-025: verifies actual Orleans inheritance without confusing shared helper services.</summary>
internal sealed class GrainSynchronizationTests
{
    [Test]
    [Arguments("NativeGrain")]
    [Arguments("Intermediate")]
    [Arguments("Orleans.Grain<SubjectState>")]
    public async Task NativeLocksInDirectAndInheritedGrainsAreRejectedAsync(string baseType)
    {
        var source = $$"""
            using NativeGrain = Orleans.Grain;
            internal abstract class Intermediate : NativeGrain { }
            internal sealed class SubjectState { }
            internal sealed class Subject : {{baseType}}
            {
                private readonly System.Threading.Lock gate = new();
                internal void Update() { lock (gate) { } }
            }
            """;

        var findings = await AnalyzerFixture.AnalyzeAsync(new TypedSynchronizationAnalyzer(), source);
        await AnalyzerFixture.AssertFindingAsync(findings, TypedSynchronizationAnalyzer.DiagnosticId,
            Microsoft.CodeAnalysis.DiagnosticSeverity.Error, source, "lock (gate)");
        await Assert.That(findings[0].Location.SourceSpan.Start)
            .IsEqualTo(source.IndexOf("lock (gate)", StringComparison.Ordinal) + "lock (".Length);
        await Assert.That(findings[0].Location.SourceSpan.Length).IsEqualTo("gate".Length);
    }

    [Test]
    [Arguments("gate.Enter()")]
    [Arguments("gate.EnterScope()")]
    [Arguments("gate.TryEnter()")]
    public async Task ExplicitNativeLockAcquisitionInGrainsIsRejectedAsync(string invocation)
    {
        var source = $$"""
            internal sealed class Subject : Orleans.Grain
            {
                private readonly System.Threading.Lock gate = new();
                internal void Update() { {{invocation}}; }
            }
            """;

        await TypedSynchronizationFixture.AssertFindingAsync(source, invocation);
    }

    [Test]
    public async Task GrainNamesAndIndependentNestedHelpersDoNotImplyActivationOwnershipAsync()
    {
        const string source = """
            using System.Threading;
            internal sealed class Grain
            {
                private readonly Lock gate = new();
                internal void Update() { lock (gate) { } }
            }
            internal sealed class Subject : Orleans.Grain
            {
                internal sealed class SharedService
                {
                    private readonly Lock gate = new();
                    internal void Update() { lock (gate) { } }
                }
                internal void Update(SharedService service) { service.Update(); }
            }
            """;

        await TypedSynchronizationFixture.AssertAllowedAsync(source);
    }

    [Test]
    public async Task ApplicationMonitorTypeDoesNotBindToNativeMonitorAsync()
    {
        const string source = """
            namespace Application;
            internal sealed class Monitor
            {
                internal int Sample() => 42;
            }
            internal sealed class Subject(Monitor monitor)
            {
                internal int Read() => monitor.Sample();
            }
            """;

        await TypedSynchronizationFixture.AssertAllowedAsync(source);
    }
}
