using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed class SiteIsolatedGitHubReceiptTests
{
    [Test]
    public async Task AC_ISO_009_ActualMetadataRetainsIndependentSourcesAndAllNativeWorkers()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var result = await ProbeAsync(inputs.Metadata, token);
        await Assert.That(result).IsTrue();
        await Assert.That(inputs.Metadata[SiteIsolatedGitHubTokens.Workers]!.AsArray().Count)
            .IsEqualTo(SiteIsolatedInventory.Workers());
        var actualSite = SiteTestInputs.Read();
        await Assert.That(inputs.Metadata[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Website]!.GetValue<string>())
            .IsEqualTo(actualSite.SiteRevision);
    }

    [Test]
    public async Task AC_ISO_009_RejectsClosedReceiptSourceIdentityExpiryAndCompletenessCorruption()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        Action<JsonObject>[] changes =
        [
            value => value[SiteIsolatedGitHubTokens.Extra] = true,
            value => value[SiteIsolatedGitHubTokens.SchemaVersion] = SiteIsolatedGitHubTokens.Zero,
            value => value[SiteIsolatedGitHubTokens.Source]![SiteIsolatedGitHubTokens.Measured] = SiteIsolatedGitHubTokens.WrongSha,
            value => value[SiteIsolatedGitHubTokens.Artifacts]![SiteIsolatedGitHubTokens.SuiteKey]![SiteIsolatedGitHubTokens.Expired] = true,
            value => value[SiteIsolatedGitHubTokens.Workers]!.AsArray().RemoveAt(SiteIsolatedGitHubTokens.Zero),
            value => value[SiteIsolatedGitHubTokens.Workers]!.AsArray().Add(value[SiteIsolatedGitHubTokens.Workers]![SiteIsolatedGitHubTokens.Zero]!.DeepClone()),
            value => value[SiteIsolatedGitHubTokens.PublishEligible] = value[SiteIsolatedGitHubTokens.Mode]!.GetValue<string>() != SiteIsolatedGitHubTokens.Publish,
        ];
        foreach (var change in changes)
        {
            var copy = inputs.Metadata.DeepClone().AsObject();
            change(copy);
            await Assert.That(await ProbeAsync(copy, token)).IsFalse();
        }
    }

    [Test]
    public async Task AC_BC_FAIL_019_ActualSourceRequiresItsExactAggregateStepContract()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var original = inputs.Metadata.DeepClone();
        var copy = inputs.Metadata.DeepClone().AsObject();
        await Assert.That(await ProbeAsync(copy, token)).IsTrue();
        var steps = copy[SiteIsolatedGitHubTokens.AggregateJob]![SiteIsolatedGitHubFields.Steps]!.AsArray();
        await Assert.That(steps.Count).IsEqualTo(3);
        steps.Insert(3, new JsonObject { [SiteIsolatedGitHubFields.Name] = "Unexpected stage one", [SiteIsolatedGitHubTokens.Conclusion] = "success" });
        steps.Insert(4, new JsonObject { [SiteIsolatedGitHubFields.Name] = "Unexpected stage two", [SiteIsolatedGitHubTokens.Conclusion] = "success" });
        await Assert.That(await ProbeAsync(copy, token)).IsFalse();
        await Assert.That(JsonNode.DeepEquals(inputs.Metadata, original)).IsTrue();
    }

    /// <summary>AC-BC-FAIL-003: a controlled receipt keeps the actual worker identity and permits only workload failure.</summary>
    [Test]
    public async Task AC_BC_FAIL_003_ReceiptAcceptsFailedWorkloadWithSuccessfulResultUpload()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var copy = FailedWorkerReceipt(inputs.Metadata);
        await Assert.That(await ProbeAsync(copy, token)).IsTrue();
        await Assert.That(JsonNode.DeepEquals(copy[SiteIsolatedGitHubFields.Preparation],
            inputs.Metadata[SiteIsolatedGitHubFields.Preparation])).IsTrue();
        await Assert.That(copy[SiteIsolatedGitHubTokens.Workers]!.AsArray().Count)
            .IsEqualTo(SiteIsolatedInventory.Workers());
    }

    /// <summary>AC-BC-FAIL-003: failure cannot be relabelled successful and failed uploads or native runtime preparation checks remain rejected.</summary>
    [Test]
    [Arguments("successConclusion")]
    [Arguments("successWorkload")]
    [Arguments("failedUpload")]
    [Arguments("failedPreparation")]
    public async Task AC_BC_FAIL_003_ReceiptRejectsFailureOutcomeMismatches(string corruption)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        var inputs = await SiteIsolatedGitHubInputs.ReadAsync(token);
        var copy = FailedWorkerReceipt(inputs.Metadata);
        var job = copy[SiteIsolatedGitHubTokens.Workers]![SiteIsolatedGitHubTokens.Zero]![SiteIsolatedGitHubFields.Job]!;
        switch (corruption)
        {
            case "successConclusion":
                job[SiteIsolatedFields.Conclusion] = "success";
                break;
            case "successWorkload":
            case "failedUpload":
                var name = corruption == "successWorkload" ? "Run database workload" : "Save benchmark results";
                var step = job[SiteIsolatedFields.Steps]!.AsArray().Single(item => item![SiteIsolatedFields.Name]!.GetValue<string>() == name)!;
                step[SiteIsolatedFields.Conclusion] = corruption == "successWorkload" ? "success" : "failure";
                break;
            case "failedPreparation":
                copy[SiteIsolatedGitHubFields.Preparation]![SiteIsolatedGitHubFields.Job]![SiteIsolatedFields.Conclusion] = "failure";
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(corruption));
        }
        await Assert.That(await ProbeAsync(copy, token)).IsFalse();
    }

    private static JsonObject FailedWorkerReceipt(JsonObject original)
    {
        var copy = original.DeepClone().AsObject();
        var job = copy[SiteIsolatedGitHubTokens.Workers]![SiteIsolatedGitHubTokens.Zero]![SiteIsolatedGitHubFields.Job]!;
        job[SiteIsolatedFields.Conclusion] = "failure";
        foreach (var step in job[SiteIsolatedFields.Steps]!.AsArray())
        {
            step![SiteIsolatedFields.Conclusion] = step[SiteIsolatedFields.Name]!.GetValue<string>() == "Run database workload"
                ? "failure" : "success";
        }
        return copy;
    }

    private static async Task<bool> ProbeAsync(JsonObject receipt, CancellationToken token)
    {
        var inputs = SiteTestInputs.Read();
        var result = await SiteIsolatedGitHubNodeProcess.RunAsync(inputs.Repository, new
        {
            operation = SiteIsolatedGitHubFields.ReceiptOperation,
            repository = inputs.Repository,
            receipt,
        }, token);
        return result.GetProperty(SiteIsolatedFields.Ok).GetBoolean();
    }
}
