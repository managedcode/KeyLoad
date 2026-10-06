using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class OpenLoopPlanJoinAssertions
{
    private const string PreflightProperty = "preflight";
    private const string LabelProperty = "label";
    private const string JobNameProperty = "jobName";
    private const string OpenLoopRateProperty = "openLoopRate";
    private const string OpenLoopProofProperty = "openLoopCancellationProof";
    private const string LabelRateSuffix = " ops/s";
    private const string MeasurementLabel = " / open-loop ";
    private const string ProofLabel = " / cancellation proof ";
    private const string MeasurementArtifact = "comparison-open-loop-worker-";
    private const string ProofArtifact = "comparison-open-loop-proof-";
    private const string MeasurementQualification = "comparison-open-loop-case-qualification-";
    private const string ProofQualification = "comparison-open-loop-proof-qualification-";
    private const string GithubSeed = "retained=sentinel";
    private const string GithubMatrixPrefix = "database_matrices=";
    private const string ScaleProfilePrefix = "scaled-";
    private const string VectorProfilePrefix = "vector-";
    private static readonly string[] ExpectedAppendedFields =
    [
        IsolatedPlanFields.Id, IsolatedPlanFields.Target, IsolatedPlanFields.NodeCount,
        IsolatedPlanFields.Scenario, IsolatedPlanFields.Profile, IsolatedPlanFields.Family,
        PreflightProperty, LabelProperty, JobNameProperty, IsolatedPlanFields.ScaleProfile,
        IsolatedPlanFields.VectorProfile, IsolatedPlanFields.ArtifactPrefix, IsolatedPlanFields.QualificationPrefix,
        OpenLoopPlanExpectedInventory.OfferedRateProperty, OpenLoopPlanExpectedInventory.CancellationProofProperty,
        OpenLoopRateProperty, OpenLoopProofProperty
    ];

    internal static async Task VerifyDefaultPreservedAsync(OpenLoopPlanCliSnapshot baseline,
        OpenLoopPlanCliSnapshot expanded, JsonElement contract)
    {
        await VerifySuccessfulCliAsync(baseline).ConfigureAwait(false);
        await VerifySuccessfulCliAsync(expanded).ConfigureAwait(false);
        await Assert.That(baseline.OpenLoopPath is null).IsTrue();
        await Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(baseline.PlanPath)!, "open-loop.json"))).IsFalse();
        await AssertBytesEqualAsync(baseline.PlanBytes, expanded.PlanBytes).ConfigureAwait(false);
        await AssertBytesEqualAsync(baseline.ScaledBytes, expanded.ScaledBytes).ConfigureAwait(false);
        await AssertBytesEqualAsync(baseline.VectorBytes, expanded.VectorBytes).ConfigureAwait(false);
        await AssertBytesEqualAsync(baseline.CompositeBytes, expanded.CompositeBytes).ConfigureAwait(false);
        await Assert.That(JsonNode.DeepEquals(baseline.Plan, expanded.Plan)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(baseline.ScaledPlans, expanded.ScaledPlans)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(baseline.VectorPlans, expanded.VectorPlans)).IsTrue();
        await IsolatedDatabaseMatrixAssertions.VerifyAsync(baseline.Matrices, baseline.Plan,
            baseline.ScaledPlans, baseline.VectorPlans).ConfigureAwait(false);
        await OpenLoopPlanCompactMatrixAssertions.VerifyAsync(baseline).ConfigureAwait(false);
        await OpenLoopPlanCompactMatrixAssertions.VerifyAsync(expanded).ConfigureAwait(false);
        await VerifyTargetMatrixAppendAsync(baseline, expanded, contract).ConfigureAwait(false);
        await VerifyMatrixGroupsAsync(expanded.Matrices).ConfigureAwait(false);
    }

    internal static async Task VerifyOpenLoopPlanAsync(OpenLoopPlanCliSnapshot snapshot, JsonElement contract,
        JsonElement scaled, CancellationToken cancellationToken)
    {
        var path = snapshot.OpenLoopPath ?? throw new InvalidOperationException("The optional plan path was not captured.");
        using var plan = JsonDocument.Parse(await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false));
        await OpenLoopPlanAssertions.VerifyCanonicalAsync(plan.RootElement, contract, scaled).ConfigureAwait(false);
        var expected = OpenLoopPlanExpectedInventory.Build(contract, scaled);
        await Assert.That(expected.Measurements.Count).IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedMeasurementCount);
        await Assert.That(expected.Proofs.Count).IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedProofCount);
    }

    internal static async Task VerifyMatrixProbeAsync(OpenLoopPlanNodeResult result, bool rejected)
    {
        await Assert.That(result.ExitCode).IsEqualTo(0).Because(result.Error);
        await Assert.That(result.Error).IsEqualTo(string.Empty);
        using var document = JsonDocument.Parse(result.Output);
        await Assert.That(document.RootElement.GetProperty(IsolatedPlanFields.Rejected).GetBoolean()).IsEqualTo(rejected);
        if (!rejected)
        {
            var counts = document.RootElement.GetProperty(IsolatedPlanFields.Counts);
            foreach (var group in WorkflowDatabaseGroups.Entries)
            {
                var expectedCount = group.Name == OpenLoopPlanExpectedInventory.KeyLoadTarget ? 207 : 201;
                await Assert.That(counts.GetProperty(group.Key).GetInt32()).IsEqualTo(expectedCount);
            }
        }
    }

    internal static async Task AssertBytesEqualAsync(byte[] expected, byte[] actual)
        => await Assert.That(actual).IsEquivalentTo(expected, TUnit.Assertions.Enums.CollectionOrdering.Matching);

    private static async Task VerifySuccessfulCliAsync(OpenLoopPlanCliSnapshot snapshot)
    {
        await Assert.That(snapshot.ExitCode).IsEqualTo(0);
        await Assert.That(snapshot.StandardError).IsEqualTo(string.Empty);
        await AssertBytesEqualAsync(snapshot.PlanBytes, Encoding.UTF8.GetBytes(snapshot.StandardOutput)).ConfigureAwait(false);
        await Assert.That(snapshot.GithubLines.Length).IsEqualTo(2);
        await Assert.That(snapshot.GithubLines[0]).IsEqualTo(GithubSeed);
        await Assert.That(snapshot.GithubLines[1].StartsWith(GithubMatrixPrefix, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task VerifyMatrixGroupsAsync(JsonObject matrices)
    {
        await Assert.That(matrices.Count).IsEqualTo(WorkflowDatabaseGroups.Entries.Length);
        await Assert.That(matrices.Select(static entry => entry.Key)
            .SequenceEqual(WorkflowDatabaseGroups.Entries.Select(static entry => entry.Key))).IsTrue();
    }

    private static async Task VerifyTargetMatrixAppendAsync(OpenLoopPlanCliSnapshot baseline,
        OpenLoopPlanCliSnapshot expanded, JsonElement contract)
    {
        var expectedTargets = contract.GetProperty(OpenLoopPlanExpectedInventory.TargetsProperty).EnumerateArray()
            .Select(static target => target.GetString()!).ToArray();
        using var scaledDocument = JsonDocument.Parse(baseline.ScaledPlans.ToJsonString());
        var (measurements, proofs, _) = OpenLoopPlanExpectedInventory.Build(contract, scaledDocument.RootElement);
        await Assert.That(measurements.Count).IsEqualTo(OpenLoopPlanExpectedInventory.ExpectedMeasurementCount);
        await Assert.That(measurements.Select(static cell => cell.Target).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(expectedTargets.Length);
        await Assert.That(measurements.GroupBy(static cell => cell.Target)
            .All(static group => group.Count() == OpenLoopPlanExpectedInventory.ExpectedPerTarget)).IsTrue();
        foreach (var group in WorkflowDatabaseGroups.Entries)
        {
            await Assert.That(expectedTargets.Contains(group.Name, StringComparer.Ordinal)).IsTrue();
            await VerifyTargetRowsAsync(baseline, expanded, group.Key, group.Name, measurements, proofs)
                .ConfigureAwait(false);
        }
    }

    private static async Task VerifyTargetRowsAsync(OpenLoopPlanCliSnapshot baseline, OpenLoopPlanCliSnapshot expanded,
        string groupKey, string target, IReadOnlyList<OpenLoopPlanExpectedCell> measurements,
        IReadOnlyList<OpenLoopPlanExpectedCell> proofs)
    {
        var prior = baseline.Matrices[groupKey]![IsolatedPlanFields.Include]!.AsArray();
        var rows = expanded.Matrices[groupKey]![IsolatedPlanFields.Include]!.AsArray();
        var targetMeasurements = measurements.Where(cell => cell.Target == target).ToArray();
        var targetProofs = proofs.Where(cell => cell.Target == target).ToArray();
        var expectedTotal = target == OpenLoopPlanExpectedInventory.KeyLoadTarget ? 207 : 201;
        await Assert.That(prior.Count).IsEqualTo(129);
        await Assert.That(rows.Count).IsEqualTo(expectedTotal);
        for (var index = 0; index < prior.Count; index++)
        {
            await Assert.That(JsonNode.DeepEquals(rows[index], prior[index])).IsTrue();
        }
        var appended = targetMeasurements.Concat(targetProofs).ToArray();
        await Assert.That(rows.Count - prior.Count).IsEqualTo(appended.Length);
        for (var index = 0; index < appended.Length; index++)
        {
            await VerifyNewRowAsync(rows[prior.Count + index]!.AsObject(), prior, appended[index]);
        }
        var allJobNames = rows.Select(row => row![JobNameProperty]!.GetValue<string>()).ToArray();
        await Assert.That(allJobNames.Distinct(StringComparer.Ordinal).Count()).IsEqualTo(expectedTotal);
        await Assert.That(allJobNames.Skip(prior.Count).Distinct(StringComparer.Ordinal).Count()).IsEqualTo(appended.Length);
    }

    private static async Task VerifyNewRowAsync(JsonObject actual, JsonArray prior,
        OpenLoopPlanExpectedCell expectedCell)
    {
        var isProof = expectedCell.CancellationProof;
        var suffix = (isProof ? OpenLoopPlanExpectedInventory.ProofSuffix : OpenLoopPlanExpectedInventory.MeasurementSuffix)
            + expectedCell.OfferedRatePerSecond.ToString(CultureInfo.InvariantCulture);
        var sourceId = expectedCell.Id[..^suffix.Length];
        var source = prior.Single(row => row![IsolatedPlanFields.Id]!.GetValue<string>() == sourceId
            && !row[PreflightProperty]!.GetValue<bool>())!.AsObject();
        var kindLabel = isProof ? ProofLabel : MeasurementLabel;
        var expectedLabel = source[LabelProperty]!.GetValue<string>() + kindLabel
            + expectedCell.OfferedRatePerSecond.ToString(CultureInfo.InvariantCulture) + LabelRateSuffix;
        var expectedJob = source[JobNameProperty]!.GetValue<string>() + kindLabel
            + expectedCell.OfferedRatePerSecond.ToString(CultureInfo.InvariantCulture) + LabelRateSuffix;
        var expected = ExpectedNewRow(expectedCell, expectedLabel, expectedJob, isProof);
        await Assert.That(actual.Select(static property => property.Key))
            .IsEquivalentTo(ExpectedAppendedFields);
        await Assert.That(JsonNode.DeepEquals(actual, expected)).IsTrue();
    }

    private static JsonObject ExpectedNewRow(OpenLoopPlanExpectedCell cell,
        string label, string jobName, bool isProof)
        => new()
        {
            [IsolatedPlanFields.Id] = cell.Id,
            [IsolatedPlanFields.Target] = cell.Target,
            [IsolatedPlanFields.NodeCount] = cell.NodeCount,
            [IsolatedPlanFields.Scenario] = cell.Scenario,
            [IsolatedPlanFields.Profile] = cell.Profile,
            [IsolatedPlanFields.Family] = cell.Family,
            [PreflightProperty] = false,
            [LabelProperty] = label,
            [JobNameProperty] = jobName,
            [IsolatedPlanFields.ScaleProfile] = cell.Profile.StartsWith(ScaleProfilePrefix, StringComparison.Ordinal)
                ? cell.Profile : null,
            [IsolatedPlanFields.VectorProfile] = cell.Profile.StartsWith(VectorProfilePrefix, StringComparison.Ordinal)
                ? cell.Profile : null,
            [IsolatedPlanFields.ArtifactPrefix] = isProof ? ProofArtifact : MeasurementArtifact,
            [IsolatedPlanFields.QualificationPrefix] = isProof ? ProofQualification : MeasurementQualification,
            [OpenLoopPlanExpectedInventory.OfferedRateProperty] = cell.OfferedRatePerSecond,
            [OpenLoopPlanExpectedInventory.CancellationProofProperty] = cell.CancellationProof,
            [OpenLoopRateProperty] = cell.OfferedRatePerSecond,
            [OpenLoopProofProperty] = cell.CancellationProof
        };
}
