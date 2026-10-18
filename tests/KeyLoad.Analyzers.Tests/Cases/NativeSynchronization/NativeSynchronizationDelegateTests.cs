namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-025: native synchronization targets cannot escape through delegate conversion.</summary>
internal sealed class NativeSynchronizationDelegateTests
{
    [Test]
    [Arguments("System.Action<object> enter = System.Threading.Monitor.Enter;", "System.Threading.Monitor.Enter")]
    [Arguments("System.Action<object> enter = GateMonitor.Enter;", "GateMonitor.Enter")]
    [Arguments("System.Action<object> enter = Enter;", "Enter")]
    [Arguments("var enter = new System.Action<object>(GateMonitor.Enter);", "GateMonitor.Enter")]
    public async Task NativeMonitorMethodGroupsRetainExactLocationsAsync(string declaration, string target)
    {
        var source = $$"""
            using GateMonitor = System.Threading.Monitor;
            using static System.Threading.Monitor;
            internal static class Subject
            {
                internal static void Execute(object payload)
                {
                    {{declaration}}
                    enter(payload);
                }
            }
            """;

        await TypedSynchronizationFixture.AssertFindingAsync(source, target);
    }

    [Test]
    [Arguments("System.Action acquire = gate.Enter;", "gate.Enter")]
    [Arguments("System.Func<bool> acquire = gate.TryEnter;", "gate.TryEnter")]
    public async Task GrainOwnedNativeLockMethodGroupsAreRejectedAsync(string declaration, string target)
    {
        var source = $$"""
            internal sealed class Subject : Orleans.Grain
            {
                private readonly System.Threading.Lock gate = new();
                internal void Execute()
                {
                    {{declaration}}
                    acquire();
                }
            }
            """;

        await TypedSynchronizationFixture.AssertFindingAsync(source, target);
    }

    [Test]
    public async Task IndependentNativeLockAndOrdinaryPayloadDelegatesAreAllowedAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                private readonly System.Threading.Lock gate = new();
                internal object Execute(object payload)
                {
                    System.Action acquire = gate.Enter;
                    acquire();
                    gate.Exit();
                    System.Func<object> identity = () => payload;
                    return identity();
                }
            }
            """;

        await TypedSynchronizationFixture.AssertAllowedAsync(source);
    }
}
