namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-029 and AC-CQ-031: general numeric scope preserves every previously narrow boundary example.</summary>
internal sealed class MagicRuntimeDurationBoundaryTests
{
    [Test]
    public async Task NativeFactoryDefaultArgumentsAreNotReportedAsSourceLiteralsAsync()
    {
        const string source = """
            using System;
            internal static class Subject
            {
                internal static TimeSpan Execute() => TimeSpan.FromSeconds(seconds: [|1L|]);
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task NonTimeoutNativeParametersAndUnrelatedNamesRequireNumericIdentitiesAsync()
    {
        const string source = """
            using System.Threading;
            internal sealed class TimeoutPolicy
            {
                internal int Delay(int value) => value;
                internal int Wait(int millisecondsTimeout) => millisecondsTimeout;
                internal int FromSeconds(int value) => value;
                internal int CancelAfter(int value) => value;
                internal int Execute()
                {
                    using var semaphore = new SemaphoreSlim([|0|], [|1|]);
                    var arithmetic = [|1|] + [|1000|];
                    return Delay([|0|]) + Wait([|1|]) + FromSeconds([|1|]) + CancelAfter([|0|]) + arithmetic;
                }
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task NativeDateTimeComponentsRequireGeneralNumericIdentitiesAsync()
    {
        const string source = """
            using System;
            internal static class Subject
            {
                internal static DateTime Execute() => new DateTime([|2026|], [|1|], [|1|]);
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }

    [Test]
    public async Task ConvertedNegativeTimeoutRetainsTheFullNumericTokenAsync()
    {
        const string source = """
            using System.Threading.Tasks;
            internal static class Subject
            {
                internal static Task Execute() => Task.Delay((int)-[|1L|]);
            }
            """;

        await MagicRuntimeFixture.AssertDurationAsync(source);
    }
}
