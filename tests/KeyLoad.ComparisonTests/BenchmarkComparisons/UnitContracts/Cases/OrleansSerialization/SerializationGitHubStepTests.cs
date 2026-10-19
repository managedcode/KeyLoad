namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>R15-AC001: classify preceding step visibility without relaxing completed-success admission.</summary>
internal sealed class SerializationGitHubStepTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EveryRequiredStepCompletedSuccessfullyIsReady(bool measured)
    {
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(SerializationGitHubStepData.Input(measured));
        await AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Ready).IsTrue();
        await Assert.That(result.Completed).IsTrue();
        await Assert.That(result.StrictError).IsEqualTo(string.Empty);
    }

    [Test]
    [Arguments(false, "queued")]
    [Arguments(false, "in_progress")]
    [Arguments(false, "pending")]
    [Arguments(true, "queued")]
    [Arguments(true, "in_progress")]
    [Arguments(true, "pending")]
    public async Task UniquePendingStepsRemainRejectedByStrictSuccessGate(bool measured, string status)
    {
        var input = SerializationGitHubStepData.Input(measured);
        foreach (var step in SerializationGitHubStepData.Steps(input))
        {
            SerializationGitHubStepData.State(step!, status, null);
        }

        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Ready).IsFalse();
        await Assert.That(result.Completed).IsFalse();
        await Assert.That(result.StrictError).IsEqualTo(SerializationGitHubStepFields.RequiredStepError);
    }

    [Test]
    [Arguments("null-job")]
    [Arguments("missing-steps")]
    [Arguments("null-steps")]
    [Arguments("object-steps")]
    [Arguments("missing-step")]
    [Arguments("null-step")]
    [Arguments("duplicate-step")]
    [Arguments("missing-name")]
    [Arguments("wrong-name")]
    [Arguments("missing-status")]
    [Arguments("missing-conclusion")]
    [Arguments("pending-then-failed")]
    [Arguments("pending-then-missing")]
    [Arguments("pending-then-duplicate")]
    public async Task MalformedOrAmbiguousStatesFailBeforeAnyVisibilityRetry(string fault)
    {
        var input = SerializationGitHubStepData.Input(measured: true);
        SerializationGitHubStepData.Fault(input, fault);
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: false);
        var expected = fault is "null-job" or "missing-steps" or "null-steps" or "object-steps"
            ? SerializationGitHubStepFields.StepsError : SerializationGitHubStepFields.RequiredStepError;
        await Assert.That(result.Error).IsEqualTo(expected);
    }

    [Test]
    [Arguments("completed", "failure")]
    [Arguments("completed", "skipped")]
    [Arguments("completed", "cancelled")]
    [Arguments("completed", "timed_out")]
    [Arguments("completed", "action_required")]
    [Arguments("completed", "neutral")]
    [Arguments("completed", null)]
    [Arguments("queued", "success")]
    [Arguments("in_progress", "failure")]
    [Arguments("pending", "success")]
    [Arguments("unknown", null)]
    public async Task TerminalOrInconsistentStatusCannotBeRetried(string status, string? conclusion)
    {
        var input = SerializationGitHubStepData.Input(measured: true);
        var steps = SerializationGitHubStepData.Steps(input);
        SerializationGitHubStepData.State(steps[steps.Count - 1]!, status, conclusion);
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: false);
        await Assert.That(result.Error).IsEqualTo(SerializationGitHubStepFields.RequiredStepError);
    }

    [Test]
    public async Task PrepareRequiresOnlyItsCompletedPrecedingSteps()
    {
        var input = SerializationGitHubStepData.Input(measured: true);
        input[SerializationGitHubStepFields.Measured] = false;
        var steps = SerializationGitHubStepData.Steps(input);
        SerializationGitHubStepData.State(steps[3]!, "in_progress", null);
        SerializationGitHubStepData.State(steps[4]!, "pending", null);
        var result = await SerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Ready).IsTrue();
        await Assert.That(result.Completed).IsTrue();
    }

    internal static async Task AssertProbeAsync(SerializationGitHubStepResponse result, bool accepted)
    {
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        await Assert.That(result.Unchanged).IsTrue();
        await Assert.That(result.Accepted).IsEqualTo(accepted);
        await Assert.That(string.IsNullOrEmpty(result.Error)).IsEqualTo(accepted);
    }
}
