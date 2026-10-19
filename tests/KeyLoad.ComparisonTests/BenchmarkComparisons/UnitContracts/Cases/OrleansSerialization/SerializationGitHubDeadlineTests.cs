using System.Text.Json.Nodes;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>R15-AC001: pure monotonic deadline arithmetic never extends the accepted visibility budget.</summary>
internal sealed class SerializationGitHubDeadlineTests
{
    [Test]
    [Arguments(0, 0, 10000)]
    [Arguments(1000, 1000, 10000)]
    [Arguments(1000, 111000, 10000)]
    [Arguments(1000, 120999, 1)]
    public async Task WaitNeverExceedsTenSecondsOrRemainingBudget(double start, double now, double expected)
    {
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(Input(start, now));
        await SerializationGitHubStepTests.AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Wait).IsEqualTo(expected);
    }

    [Test]
    [Arguments(0, 120000, SerializationGitHubStepFields.DeadlineError)]
    [Arguments(1000, 121001, SerializationGitHubStepFields.DeadlineError)]
    [Arguments(-1, 0, SerializationGitHubStepFields.ClockError)]
    [Arguments(1000, 999, SerializationGitHubStepFields.ClockError)]
    public async Task ExpiredOrReversedClockFailsClosed(double start, double now, string error)
    {
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(Input(start, now));
        await SerializationGitHubStepTests.AssertProbeAsync(result, accepted: false);
        await Assert.That(result.Error).IsEqualTo(error);
    }

    [Test]
    public async Task NonfiniteClockFailsClosed()
    {
        var input = Input(0, 0);
        input[SerializationGitHubStepFields.Nonfinite] = true;
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(input);
        await SerializationGitHubStepTests.AssertProbeAsync(result, accepted: false);
        await Assert.That(result.Error).IsEqualTo(SerializationGitHubStepFields.ClockError);
    }

    private static JsonObject Input(double start, double now)
        => new()
        {
            [SerializationGitHubStepFields.Mode] = "deadline",
            [SerializationGitHubStepFields.Start] = start,
            [SerializationGitHubStepFields.Now] = now
        };
}
