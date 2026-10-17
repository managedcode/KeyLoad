namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: named constants cannot hide operational deadlines.</summary>
internal sealed class TypedConfigurationDurationTests
{
    [Test]
    public async Task CanonicalValidationComparesNamedPrimitiveBoundsWithoutExecutingADeadlineAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                private const int DefaultDeadlineSeconds = 30;
                private const long MinimumDeadlineTicks = System.TimeSpan.TicksPerSecond;
                private const int MaximumDeadlineMinutes = 140;
                private const long MaximumDeadlineTicks = MaximumDeadlineMinutes * System.TimeSpan.TicksPerMinute;
                public System.TimeSpan Deadline { get; set; } = System.TimeSpan.FromSeconds(DefaultDeadlineSeconds);
                internal bool IsValid() => Deadline.Ticks >= MinimumDeadlineTicks && Deadline.Ticks <= MaximumDeadlineTicks;
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task ACanonicalOptionsDefaultMarkerDoesNotGrantRuntimeExecutionPolicyAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                private const int HiddenDeadlineSeconds = 15;
                public System.TimeSpan Deadline { get; set; } = System.TimeSpan.FromSeconds(HiddenDeadlineSeconds);
                internal System.Threading.Tasks.Task Execute()
                {
                    var deadline = [|System.TimeSpan.FromSeconds(HiddenDeadlineSeconds)|];
                    return [|System.Threading.Tasks.Task.Delay(HiddenDeadlineSeconds)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ACanonicalOptionsConstructorCannotExecuteAHardcodedDeadlineAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy
            {
                private const int HiddenDeadlineMilliseconds = 15;
                public System.TimeSpan Deadline { get; set; }
                internal Policy()
                {
                    Deadline = System.TimeSpan.FromMilliseconds(HiddenDeadlineMilliseconds);
                    [|System.Threading.Tasks.Task.Delay(HiddenDeadlineMilliseconds)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ScreenshotConstAndStaticReadonlyDeadlineRemainHardcodedAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int DispatchDeadlineSeconds = 15;
                private static readonly System.TimeSpan DispatchDeadline =
                    [|System.TimeSpan.FromSeconds(DispatchDeadlineSeconds)|];
                internal static System.Threading.Tasks.Task Execute() =>
                    [|System.Threading.Tasks.Task.Delay(DispatchDeadline)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeExplicitTimeoutConstArgumentsAreStillOperationalPolicyAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int ImmediateTimeout = 0;
                internal static void Execute(System.Threading.Tasks.Task task, System.Threading.SemaphoreSlim gate)
                {
                    [|task.Wait(ImmediateTimeout)|];
                    [|gate.WaitAsync(ImmediateTimeout)|];
                    using var source = [|new System.Threading.CancellationTokenSource(ImmediateTimeout)|];
                    [|source.CancelAfter(ImmediateTimeout)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeFixedTemporalIdentitiesKeepTheirFrameworkMeaningAsync()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Execute(System.Threading.Tasks.Task task)
                {
                    task.Wait(System.TimeSpan.Zero);
                    task.Wait(System.Threading.Timeout.Infinite);
                    task.WaitAsync(System.Threading.Timeout.InfiniteTimeSpan);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task BindingMetadataCannotIntroducePerClassTimeoutFallbacksAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationBinding]
            internal static class Subject
            {
                private const int DefaultDeadlineSeconds = 15;
                internal static System.TimeSpan Bind() => [|System.TimeSpan.FromSeconds(DefaultDeadlineSeconds)|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
