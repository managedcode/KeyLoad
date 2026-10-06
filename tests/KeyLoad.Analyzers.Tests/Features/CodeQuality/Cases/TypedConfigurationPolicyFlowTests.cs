namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: actual call bindings and authored return flow cannot hide deadlines.</summary>
internal sealed class TypedConfigurationPolicyFlowTests
{
    [Test]
    public async Task AnActuallyOmittedOptionalMethodDeadlineIsRejectedAtItsNativeSinkAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int DefaultDeadline = 15;
                internal static System.Threading.Tasks.Task Execute(int timeout = DefaultDeadline) =>
                    [|System.Threading.Tasks.Task.Delay(timeout)|];
                internal static System.Threading.Tasks.Task Run() => Execute();
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task AnActuallyOmittedConstructorDeadlineIsRejectedAtItsNativeSinkAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                private const int DefaultDeadline = 15;
                internal Subject(int timeout = DefaultDeadline) { [|System.Threading.Tasks.Task.Delay(timeout)|]; }
                internal static Subject Create() => new Subject();
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task GettersAndHelperReturnsRetainTheOriginalConstDeadlineAsync()
    {
        const string source = """
            internal static class Fields { internal const int DefaultDeadline = 15; }
            internal static class Subject
            {
                private static int Deadline { get { return Fields.DefaultDeadline; } }
                private static int Resolve() => Deadline;
                internal static System.Threading.Tasks.Task Run() => [|System.Threading.Tasks.Task.Delay(Resolve())|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualOptionalHelperBindingsDistinguishOmittedDefaultsFromSuppliedDataAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int DefaultDeadline = 15;
                private static int Resolve(int timeout = DefaultDeadline) => timeout;
                internal static System.Threading.Tasks.Task Omitted() => [|System.Threading.Tasks.Task.Delay(Resolve())|];
                internal static System.Threading.Tasks.Task Supplied(int timeout) => System.Threading.Tasks.Task.Delay(Resolve(timeout));
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ExplicitDynamicInputDoesNotBecomeAnUninvokedOptionalDefaultAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int DefaultDeadline = 15;
                private static System.Threading.Tasks.Task Execute(int timeout = DefaultDeadline) => System.Threading.Tasks.Task.Delay(timeout);
                internal static System.Threading.Tasks.Task Run(int suppliedTimeout) => Execute(suppliedTimeout);
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ConfiguredGettersHelperReturnsAndNestedFunctionsRetainDynamicValuesAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int Timeout { get; init; } = 15; }
            internal sealed class Subject
            {
                private const int UnrelatedDeadline = 15;
                private readonly Policy snapshot;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> configured) => snapshot = configured.Value;
                private int Deadline => snapshot.Timeout;
                private static int Resolve(int timeout)
                {
                    int UnusedNested() { return UnrelatedDeadline; }
                    return timeout;
                }
                internal System.Threading.Tasks.Task Run() => System.Threading.Tasks.Task.Delay(Resolve(Deadline));
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task SourceCyclesDoNotInventAConstantAndNativeSentinelReturnsKeepTheirMeaningAsync()
    {
        const string source = """
            internal static class Subject
            {
                private static int First(int value) => Second(value);
                private static int Second(int value) => First(value);
                private static System.TimeSpan Infinite => System.Threading.Timeout.InfiniteTimeSpan;
                private static System.TimeSpan Zero() => System.TimeSpan.Zero;
                internal static System.Threading.Tasks.Task Inspect(int supplied) => System.Threading.Tasks.Task.Delay(First(supplied));
                internal static void Poll(System.Threading.Tasks.Task task)
                {
                    task.Wait(Zero());
                    task.WaitAsync(Infinite);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
