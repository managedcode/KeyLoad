using System.Text.Json.Nodes;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>R15-AC001: pure monotonic deadline arithmetic never extends the accepted visibility budget.</summary>
internal sealed class NativeSerializationGitHubDeadlineTests
{
    [Test]
    [Arguments(0, 0, 10000)]
    [Arguments(1000, 1000, 10000)]
    [Arguments(1000, 111000, 10000)]
    [Arguments(1000, 120999, 1)]
    public async Task WaitNeverExceedsTenSecondsOrRemainingBudget(double start, double now, double expected)
    {
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(Input(start, now));
        await NativeSerializationGitHubStepTests.AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Wait).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0, 120000, NativeSerializationGitHubStepFields.DeadlineError)]
    [Arguments(1000, 121001, NativeSerializationGitHubStepFields.DeadlineError)]
    [Arguments(-1, 0, NativeSerializationGitHubStepFields.ClockError)]
    [Arguments(1000, 999, NativeSerializationGitHubStepFields.ClockError)]
    public async Task ExpiredOrReversedClockFailsClosed(double start, double now, string error)
    {
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(Input(start, now));
        await NativeSerializationGitHubStepTests.AssertProbeAsync(result, accepted: false);
        await Assert.That(result.Error).IsEqualTo(error);
    }

    [Test]
    public async Task NonfiniteClockFailsClosed()
    {
        var input = Input(0, 0);
        input[NativeSerializationGitHubStepFields.Nonfinite] = true;
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(input);
        await NativeSerializationGitHubStepTests.AssertProbeAsync(result, accepted: false);
        await Assert.That(result.Error).IsEqualTo(NativeSerializationGitHubStepFields.ClockError);
    }

    private static JsonObject Input(double start, double now)
        => new()
        {
            [NativeSerializationGitHubStepFields.Mode] = "deadline",
            [NativeSerializationGitHubStepFields.Start] = start,
            [NativeSerializationGitHubStepFields.Now] = now
        };
}
