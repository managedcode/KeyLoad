namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: exact native synchronization identities preserve scheduling semantics.</summary>
internal sealed class StructuralNativePolicyTests
{
    [Test]
    public async Task NativeNonblockingBooleanWaitPreservesTryAcquireAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int NonblockingWaitMilliseconds = 0;
                internal static System.Threading.Tasks.Task<bool> Execute(System.Threading.SemaphoreSlim gate,
                    System.Threading.CancellationToken cancellationToken) =>
                    gate.WaitAsync(NonblockingWaitMilliseconds, cancellationToken);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task ZeroDelayAndPositiveSemaphoreTimeoutRemainOperationalAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int NoDelayMilliseconds = 0;
                private const int DeadlineMilliseconds = 30;
                internal static void Execute(System.Threading.SemaphoreSlim gate,
                    System.Threading.CancellationToken cancellationToken)
                {
                    [|System.Threading.Tasks.Task.Delay(NoDelayMilliseconds, cancellationToken)|];
                    [|gate.WaitAsync(DeadlineMilliseconds, cancellationToken)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task OneSlotBooleanDropWriteSingleReaderIsACoalescedWakeSignalAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int WakeSignalSlots = 1;
                internal static void Execute()
                {
                    System.Threading.Channels.Channel.CreateBounded<bool>(
                        new System.Threading.Channels.BoundedChannelOptions(WakeSignalSlots)
                        {
                            FullMode = System.Threading.Channels.BoundedChannelFullMode.DropWrite,
                            SingleReader = true
                        });
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    [Arguments("int", "1", "DropWrite", "true")]
    [Arguments("bool", "64", "DropWrite", "true")]
    [Arguments("bool", "1", "Wait", "true")]
    [Arguments("bool", "1", "DropWrite", "false")]
    public async Task DataBacklogsAndDifferentChannelShapesRemainConfiguredPolicyAsync(
        string itemType, string slots, string fullMode, string singleReader)
    {
        var source = $$"""
            internal static class Subject
            {
                private const int Capacity = {{slots}};
                internal static void Execute()
                {
                    System.Threading.Channels.Channel.CreateBounded<{{itemType}}>(
                        [|new System.Threading.Channels.BoundedChannelOptions(Capacity)
                        {
                            FullMode = System.Threading.Channels.BoundedChannelFullMode.{{fullMode}},
                            SingleReader = {{singleReader}}
                        }|]);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
