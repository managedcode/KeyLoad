using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopPlanCliJoinTests
{
    private const string ExistingOpenLoopOutputFailure = "Isolated comparison planning failed.";
    private const string OpenLoopOutputArgument = "--open-loop-output=";
    private const string GithubOutputArgument = "--github-output=";
    private const string InvalidRateArgument = "--open-loop-rate=500";

    [Test]
    public async Task AcScale018OptionalCliPlanPreservesLegacyFilesAndAppendsCanonicalMatrices()
    {
        var executionOptions = OpenLoopPlanProcessOptionsBinding.Capture();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await OpenLoopPlanTestLifetime.RunAsync(async (planPath, token) =>
        {
            var root = Path.GetDirectoryName(planPath)!;
            var baseline = await OpenLoopPlanCliCapture.CaptureAsync(executionOptions,
                Path.Combine(root, "baseline"), includeOpenLoop: false, token).ConfigureAwait(false);
            var expanded = await OpenLoopPlanCliCapture.CaptureAsync(executionOptions,
                Path.Combine(root, "expanded"), includeOpenLoop: true, token).ConfigureAwait(false);
            using var contract = await OpenLoopPlanNodeProcess.ReadContractAsync(token).ConfigureAwait(false);
            using var scaled = await ReadScaledPlanAsync(executionOptions, token).ConfigureAwait(false);
            await OpenLoopPlanJoinAssertions.VerifyDefaultPreservedAsync(baseline, expanded,
                contract.RootElement).ConfigureAwait(false);
            await OpenLoopPlanJoinAssertions.VerifyOpenLoopPlanAsync(expanded, contract.RootElement,
                scaled.RootElement, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task AcScale018RejectsBadSelectionsAndOwnedPathsThenRunsHealthyCli()
    {
        var executionOptions = OpenLoopPlanProcessOptionsBinding.Capture();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        await OpenLoopPlanTestLifetime.RunAsync(async (planPath, token) =>
        {
            var root = Path.GetDirectoryName(planPath)!;
            var valid = await OpenLoopPlanCliCapture.CaptureAsync(executionOptions,
                Path.Combine(root, "valid"), includeOpenLoop: true, token).ConfigureAwait(false);
            var openLoopPath = valid.OpenLoopPath!;
            var originalPlan = await File.ReadAllBytesAsync(openLoopPath, token).ConfigureAwait(false);
            var originalGithub = await File.ReadAllBytesAsync(valid.GithubPath, token).ConfigureAwait(false);
            await RejectUnknownSelectorAsync(executionOptions, Path.Combine(root, "malformed.json"), token)
                .ConfigureAwait(false);
            await RejectCreateOnlyReuseAsync(executionOptions, openLoopPath, valid.GithubPath,
                originalPlan, originalGithub, token).ConfigureAwait(false);
            await RejectSymlinkParentAsync(executionOptions, root, token).ConfigureAwait(false);
            await RejectMutatedMatrixSelectionsAsync(executionOptions, JsonNode.Parse(originalPlan)!.AsObject(), token)
                .ConfigureAwait(false);
            await OpenLoopPlanJoinAssertions.AssertBytesEqualAsync(originalPlan,
                await File.ReadAllBytesAsync(openLoopPath, token).ConfigureAwait(false)).ConfigureAwait(false);
            await OpenLoopPlanJoinAssertions.AssertBytesEqualAsync(originalGithub,
                await File.ReadAllBytesAsync(valid.GithubPath, token).ConfigureAwait(false)).ConfigureAwait(false);
            var healthy = await OpenLoopPlanCliCapture.CaptureAsync(executionOptions,
                Path.Combine(root, "healthy"), includeOpenLoop: true, token).ConfigureAwait(false);
            using var contract = await OpenLoopPlanNodeProcess.ReadContractAsync(token).ConfigureAwait(false);
            using var scaled = await ReadScaledPlanAsync(executionOptions, token).ConfigureAwait(false);
            await OpenLoopPlanJoinAssertions.VerifyOpenLoopPlanAsync(healthy, contract.RootElement,
                scaled.RootElement, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadScaledPlanAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, CancellationToken cancellationToken)
    {
        var result = await OpenLoopPlanNodeProcess.RunAsync(executionOptions, OpenLoopPlanNodeProgram.ScaledOperation,
            input: null, outputPath: null, keepStandardInputOpen: false, ready: null,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        return JsonDocument.Parse(result.Output);
    }

    private static async Task RejectUnknownSelectorAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string outputPath, CancellationToken cancellationToken)
    {
        var result = await OpenLoopPlanJoinNodeProcess.RunCliAsync(executionOptions,
            [OpenLoopOutputArgument + outputPath, InvalidRateArgument], cancellationToken).ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await Assert.That(result.Error.Trim()).IsEqualTo(ExistingOpenLoopOutputFailure);
        await Assert.That(File.Exists(outputPath)).IsFalse();
    }

    private static async Task RejectCreateOnlyReuseAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string openLoopPath, string githubPath, byte[] originalPlan, byte[] originalGithub,
        CancellationToken cancellationToken)
    {
        var result = await OpenLoopPlanJoinNodeProcess.RunCliAsync(executionOptions,
            [OpenLoopOutputArgument + openLoopPath, GithubOutputArgument + githubPath], cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(result.ExitCode).IsEqualTo(1);
        await Assert.That(result.Output).IsEqualTo(string.Empty);
        await Assert.That(result.Error.Trim()).IsEqualTo(ExistingOpenLoopOutputFailure);
        await OpenLoopPlanJoinAssertions.AssertBytesEqualAsync(originalPlan,
            await File.ReadAllBytesAsync(openLoopPath, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        await OpenLoopPlanJoinAssertions.AssertBytesEqualAsync(originalGithub,
            await File.ReadAllBytesAsync(githubPath, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
    }

    private static async Task RejectSymlinkParentAsync(IOptions<OpenLoopPlanProcessOptions> executionOptions,
        string root, CancellationToken cancellationToken)
    {
        var outside = Path.Combine(Path.GetTempPath(), "keyload-openloop-target-" + Guid.NewGuid().ToString("N"));
        var link = Path.Combine(root, "linked-output-parent");
        if (Directory.Exists(outside) || File.Exists(outside))
        {
            throw new IOException("The symlink target path already exists.");
        }
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "existing-artifact.json");
        var sentinelBytes = "retained-sentinel"u8.ToArray();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await File.WriteAllBytesAsync(sentinel, sentinelBytes, cancellationToken).ConfigureAwait(false);
            Directory.CreateSymbolicLink(link, outside);
            var outputPath = Path.Combine(link, "new-artifact.json");
            var result = await OpenLoopPlanJoinNodeProcess.RunCliAsync(executionOptions,
                [OpenLoopOutputArgument + outputPath], cancellationToken).ConfigureAwait(false);
            await Assert.That(result.ExitCode).IsEqualTo(1);
            await Assert.That(result.Output).IsEqualTo(string.Empty);
            await Assert.That(result.Error.Trim()).IsEqualTo(ExistingOpenLoopOutputFailure);
            await Assert.That(File.Exists(outputPath)).IsFalse();
            await OpenLoopPlanJoinAssertions.AssertBytesEqualAsync(sentinelBytes,
                await File.ReadAllBytesAsync(sentinel, cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() =>
        {
            if (Directory.Exists(link))
            {
                Directory.Delete(link);
            }
        }, failures);
        ServerFailureObserver.Observe(() => Directory.Delete(outside, recursive: true), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RejectMutatedMatrixSelectionsAsync(
        IOptions<OpenLoopPlanProcessOptions> executionOptions, JsonObject canonical,
        CancellationToken cancellationToken)
    {
        var rate = Mutate(canonical, cell => cell[OpenLoopPlanExpectedInventory.OfferedRateProperty]
            = OpenLoopPlanExpectedInventory.InvalidOfferedRate);
        var target = Mutate(canonical, cell => cell[OpenLoopPlanExpectedInventory.TargetProperty] = "not-a-target");
        var scenario = Mutate(canonical, cell => cell[OpenLoopPlanExpectedInventory.ScenarioProperty] = "not-a-scenario");
        foreach (var candidate in new[] { rate, target, scenario })
        {
            var request = new JsonObject { ["openLoopPlan"] = candidate };
            var result = await OpenLoopPlanJoinNodeProcess.RunMatrixProbeAsync(executionOptions,
                request.ToJsonString(), cancellationToken).ConfigureAwait(false);
            await OpenLoopPlanJoinAssertions.VerifyMatrixProbeAsync(result, rejected: true).ConfigureAwait(false);
        }
        var validRequest = new JsonObject { ["openLoopPlan"] = canonical.DeepClone() };
        var valid = await OpenLoopPlanJoinNodeProcess.RunMatrixProbeAsync(executionOptions,
            validRequest.ToJsonString(), cancellationToken).ConfigureAwait(false);
        await OpenLoopPlanJoinAssertions.VerifyMatrixProbeAsync(valid, rejected: false).ConfigureAwait(false);
    }

    private static JsonObject Mutate(JsonObject canonical, Action<JsonObject> change)
    {
        var candidate = (JsonObject)canonical.DeepClone();
        change(candidate[OpenLoopPlanExpectedInventory.MeasurementCellsProperty]![0]!.AsObject());
        return candidate;
    }
}
