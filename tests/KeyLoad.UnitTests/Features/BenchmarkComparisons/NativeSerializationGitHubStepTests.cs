namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>R15-AC001: classify preceding step visibility without relaxing completed-success admission.</summary>
internal sealed class NativeSerializationGitHubStepTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task EveryRequiredStepCompletedSuccessfullyIsReady(bool measured)
    {
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(NativeSerializationGitHubStepData.Input(measured));
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
        var input = NativeSerializationGitHubStepData.Input(measured);
        foreach (var step in NativeSerializationGitHubStepData.Steps(input))
        {
            NativeSerializationGitHubStepData.State(step!, status, null);
        }

        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Ready).IsFalse();
        await Assert.That(result.Completed).IsFalse();
        await Assert.That(result.StrictError).IsEqualTo(NativeSerializationGitHubStepFields.RequiredStepError);
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
        var input = NativeSerializationGitHubStepData.Input(measured: true);
        NativeSerializationGitHubStepData.Fault(input, fault);
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: false);
        var expected = fault is "null-job" or "missing-steps" or "null-steps" or "object-steps"
            ? NativeSerializationGitHubStepFields.StepsError : NativeSerializationGitHubStepFields.RequiredStepError;
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
        var input = NativeSerializationGitHubStepData.Input(measured: true);
        var steps = NativeSerializationGitHubStepData.Steps(input);
        NativeSerializationGitHubStepData.State(steps[steps.Count - 1]!, status, conclusion);
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: false);
        await Assert.That(result.Error).IsEqualTo(NativeSerializationGitHubStepFields.RequiredStepError);
    }

    [Test]
    public async Task PrepareRequiresOnlyItsCompletedPrecedingSteps()
    {
        var input = NativeSerializationGitHubStepData.Input(measured: true);
        input[NativeSerializationGitHubStepFields.Measured] = false;
        var steps = NativeSerializationGitHubStepData.Steps(input);
        NativeSerializationGitHubStepData.State(steps[3]!, "in_progress", null);
        NativeSerializationGitHubStepData.State(steps[4]!, "pending", null);
        var result = await NativeSerializationGitHubStepNodeProcess.ProbeAsync(input);
        await AssertProbeAsync(result, accepted: true);
        await Assert.That(result.Ready).IsTrue();
        await Assert.That(result.Completed).IsTrue();
    }

    internal static async Task AssertProbeAsync(NativeSerializationGitHubStepResponse result, bool accepted)
    {
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardError).IsEqualTo(string.Empty);
        await Assert.That(result.Unchanged).IsTrue();
        await Assert.That(result.Accepted).IsEqualTo(accepted);
        await Assert.That(string.IsNullOrEmpty(result.Error)).IsEqualTo(accepted);
    }
}
