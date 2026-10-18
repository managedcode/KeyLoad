namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: exact native timer intervals are configured execution policy.</summary>
internal sealed class TypedConfigurationTimerTests
{
    [Test]
    public async Task NativeTimerConstructorsChangeAndIntervalSettersRejectConstPolicyAsync()
    {
        const string source = """
            using NativeTimer = System.Threading.Timer;
            internal static class Subject
            {
                private const int DeadlineMilliseconds = 15;
                private const int PayloadIdentity = 7;
                [KeyLoad.ImmutableTemporalData]
                private static readonly System.TimeSpan CorpusWidth = System.TimeSpan.FromMilliseconds(DeadlineMilliseconds);
                internal static void Execute(System.Threading.TimerCallback callback)
                {
                    using var timer = [|new NativeTimer(callback, PayloadIdentity,
                        dueTime: DeadlineMilliseconds, period: DeadlineMilliseconds)|];
                    [|timer.Change(CorpusWidth, CorpusWidth)|];
                    using var periodic = [|new System.Threading.PeriodicTimer(CorpusWidth)|];
                    [|periodic.Period = CorpusWidth|];
                    using var elapsed = [|new System.Timers.Timer(DeadlineMilliseconds)|];
                    [|elapsed.Interval = DeadlineMilliseconds|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task CapturedOptionsReachEveryNativeTimerWithoutTreatingStateAsPolicyAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public System.TimeSpan Interval { get; init; } = System.TimeSpan.FromMilliseconds(15); }
            internal static class Subject
            {
                private const int PayloadIdentity = 7;
                internal static void Execute(Microsoft.Extensions.Options.IOptions<Policy> configured,
                    System.Threading.TimerCallback callback)
                {
                    var interval = configured.Value.Interval;
                    using var timer = new System.Threading.Timer(callback, PayloadIdentity, interval, interval);
                    timer.Change(interval, interval);
                    using var periodic = new System.Threading.PeriodicTimer(interval, System.TimeProvider.System);
                    periodic.Period = interval;
                    using var elapsed = new System.Timers.Timer(interval.TotalMilliseconds);
                    elapsed.Interval = interval.TotalMilliseconds;
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task ImmutableCorpusIntervalsAndApplicationTimerNamesKeepTheirMeaningAsync()
    {
        const string source = """
            internal static class Corpus
            {
                private const int BucketMinutes = 5;
                [KeyLoad.ImmutableTemporalData]
                internal static readonly System.TimeSpan Width = System.TimeSpan.FromMinutes(BucketMinutes);
            }
            internal sealed class Timer(System.TimeSpan interval)
            {
                internal System.TimeSpan Interval { get; set; } = interval;
                internal void Change(System.TimeSpan value) => Interval = value;
            }
            internal static class Subject
            {
                internal static void Execute()
                {
                    var timer = new Timer(Corpus.Width);
                    timer.Change(Corpus.Width);
                    timer.Interval = Corpus.Width;
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeDisabledTimerIdentityDoesNotBecomeADeploymentDeadlineAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int PayloadIdentity = 7;
                internal static void Execute(System.Threading.TimerCallback callback)
                {
                    using var timer = new System.Threading.Timer(callback, PayloadIdentity,
                        System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                    timer.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
