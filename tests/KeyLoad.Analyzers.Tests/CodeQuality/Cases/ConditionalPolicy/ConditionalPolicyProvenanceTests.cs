namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: only conditional result values contribute to native deadline provenance.</summary>
internal sealed class ConditionalPolicyProvenanceTests
{
    [Test]
    public async Task NativeDeadlineSinksFindConstantsInConditionalCoalesceAndSwitchResultsAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int HiddenDeadlineMilliseconds = 15;
                internal static System.Threading.Tasks.Task Conditional(bool choose, int supplied) =>
                    [|System.Threading.Tasks.Task.Delay(choose ? HiddenDeadlineMilliseconds : supplied)|];
                internal static System.Threading.Tasks.Task Coalesce(int? supplied) =>
                    [|System.Threading.Tasks.Task.Delay(supplied ?? HiddenDeadlineMilliseconds)|];
                internal static System.Threading.Tasks.Task Switch(int mode, int supplied) =>
                    [|System.Threading.Tasks.Task.Delay(mode switch { 0 => HiddenDeadlineMilliseconds, _ => supplied })|];
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task DynamicAndOptionsResultsAndNativeTimeSpanSentinelsRemainAllowedAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public System.TimeSpan Timeout { get; init; } }
            internal static class Subject
            {
                private const int PredicateOnlyDeadline = 15;
                private const int PatternTag = 1;
                internal static System.Threading.Tasks.Task Conditional(
                    Microsoft.Extensions.Options.IOptions<Policy> configured, bool choose, System.TimeSpan supplied) =>
                    System.Threading.Tasks.Task.Delay(choose ? configured.Value.Timeout : supplied);
                internal static System.Threading.Tasks.Task Coalesce(
                    Microsoft.Extensions.Options.IOptions<Policy> configured, System.TimeSpan? supplied) =>
                    System.Threading.Tasks.Task.Delay(supplied ?? configured.Value.Timeout);
                internal static System.Threading.Tasks.Task Switch(
                    Microsoft.Extensions.Options.IOptions<Policy> configured, int mode, System.TimeSpan supplied) =>
                    System.Threading.Tasks.Task.Delay(mode switch { 0 => configured.Value.Timeout, _ => supplied });
                internal static System.Threading.Tasks.Task ConditionalSentinel(bool choose, System.TimeSpan supplied) =>
                    System.Threading.Tasks.Task.Delay(choose ? supplied : System.TimeSpan.Zero);
                internal static System.Threading.Tasks.Task InfiniteSentinel(bool choose, int supplied) =>
                    System.Threading.Tasks.Task.Delay(choose ? supplied : System.Threading.Timeout.Infinite);
                internal static System.Threading.Tasks.Task CoalesceSentinel(System.TimeSpan? supplied) =>
                    System.Threading.Tasks.Task.Delay(supplied ?? System.Threading.Timeout.InfiniteTimeSpan);
                internal static System.Threading.Tasks.Task SwitchSentinel(int mode, System.TimeSpan supplied) =>
                    System.Threading.Tasks.Task.Delay(mode switch { 0 => supplied, _ => System.TimeSpan.Zero });
                internal static System.Threading.Tasks.Task PredicateOnly(bool ready, int supplied) =>
                    System.Threading.Tasks.Task.Delay(PredicateOnlyDeadline > 0 && ready ? supplied : supplied);
                internal static System.Threading.Tasks.Task PatternOnly(int mode, int supplied) =>
                    System.Threading.Tasks.Task.Delay(mode switch { PatternTag => supplied, _ => supplied });
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
