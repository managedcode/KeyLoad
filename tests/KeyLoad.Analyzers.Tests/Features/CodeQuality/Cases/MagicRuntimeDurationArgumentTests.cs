namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-029 and AC-CQ-031: native duration policy literals produce exact located errors.</summary>
internal sealed class MagicRuntimeDurationArgumentTests
{
    [Test]
    [Arguments("new TimeSpan(ticks: [|1L|])")]
    [Arguments("new TimeSpan([|0|], [|0|], [|1|])")]
    [Arguments("new TimeSpan([|0|], [|1|], [|0|], [|0|], [|0|])")]
    [Arguments("TimeSpan.FromSeconds(value: [|0d|])")]
    [Arguments("TimeSpan.FromMilliseconds([|1d|])")]
    [Arguments("TimeSpan.FromTicks([|1L|])")]
    [Arguments("TimeSpan.FromHours([|1d|])")]
    [Arguments("TimeSpan.FromMinutes([|0d|])")]
    [Arguments("TimeSpan.FromDays([|1d|])")]
    [Arguments("TimeSpan.FromMicroseconds([|1d|])")]
    public async Task TimeSpanConstructorsAndNativeFactoriesRejectInlineValuesAsync(string operation)
    {
        var source = $$"""
            using TimeSpan = System.TimeSpan;
            internal static class Subject
            {
                internal static void Execute() { {{operation}}; }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    [Arguments("Task.Delay([|0|])")]
    [Arguments("Task.Delay(millisecondsDelay: [|1|], cancellationToken: cancellationToken)")]
    [Arguments("Task.Delay([|1|] * [|1000|], cancellationToken)")]
    [Arguments("Task.Delay((int)[|1L|])")]
    [Arguments("task.Wait(millisecondsTimeout: [|0|], cancellationToken: cancellationToken)")]
    [Arguments("task.WaitAsync(timeout: TimeSpan.FromSeconds([|1d|]), cancellationToken: cancellationToken)")]
    [Arguments("task.WaitAsync(TimeSpan.FromSeconds([|1d|]), TimeProvider.System, cancellationToken)")]
    [Arguments("genericTask.WaitAsync(timeout: TimeSpan.FromSeconds([|1d|]), cancellationToken: cancellationToken)")]
    [Arguments("gate.Wait(millisecondsTimeout: [|0|], cancellationToken: cancellationToken)")]
    [Arguments("gate.WaitAsync([|1|], cancellationToken)")]
    [Arguments("gate.WaitAsync(TimeSpan.FromSeconds([|1d|]), cancellationToken)")]
    [Arguments("new CancellationTokenSource(millisecondsDelay: [|0|])")]
    [Arguments("new CancellationTokenSource(TimeSpan.FromMilliseconds([|1d|]))")]
    public async Task NativeTimeoutOverloadsBindAliasesCancellationAndArithmeticAsync(string operation)
    {
        var source = $$"""
            using System;
            using System.Threading;
            using Task = System.Threading.Tasks.Task;
            internal static class Subject
            {
                internal static void Execute(Task task, System.Threading.Tasks.Task<int> genericTask,
                    SemaphoreSlim gate, CancellationToken cancellationToken)
                {
                    {{operation}};
                }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    [Arguments("source.CancelAfter(millisecondsDelay: [|1|])")]
    [Arguments("source.CancelAfter(TimeSpan.FromMilliseconds([|1d|] + [|0d|]))")]
    public async Task NativeCancelAfterRejectsDirectAndNestedInlinePolicyAsync(string operation)
    {
        var source = $$"""
            using System;
            using System.Threading;
            internal static class Subject
            {
                internal static void Execute(CancellationTokenSource source) { {{operation}}; }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task NamedZeroOneAndArithmeticConstantsPreserveNativeOverloadCallsAsync()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            internal static class Subject
            {
                private const int ImmediateTimeout = 0;
                private const int InitialDelay = 1;
                private const int MillisecondsPerSecond = 1000;
                private const long MinimumTicks = 1L;
                internal static void Execute(Task task, SemaphoreSlim gate, CancellationToken cancellationToken)
                {
                    new TimeSpan(MinimumTicks);
                    TimeSpan.FromSeconds(InitialDelay);
                    Task.Delay(InitialDelay * MillisecondsPerSecond, cancellationToken);
                    task.Wait(ImmediateTimeout, cancellationToken);
                    task.WaitAsync(TimeSpan.FromSeconds(InitialDelay), cancellationToken);
                    gate.WaitAsync(ImmediateTimeout, cancellationToken);
                    using var source = new CancellationTokenSource(ImmediateTimeout);
                    source.CancelAfter(InitialDelay);
                }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }
}
