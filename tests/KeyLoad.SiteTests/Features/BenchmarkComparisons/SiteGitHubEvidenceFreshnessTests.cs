using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Requires the final publication selection to preserve the complete measured evidence tuple.</summary>
internal sealed class SiteGitHubEvidenceFreshnessTests
{
    [Test]
    public async Task AC_BC_028_AcceptsUnchangedPublishableMeasurementAfterArchiveVerification()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var before = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(before.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(before, scope.BeforeReceipt, token);
        var archive = await scope.VerifyArchiveAsync(scope.BeforeReceipt, token);
        await Assert.That(archive.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(archive, scope.BeforeReceipt, token);
        var after = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(after.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(after, scope.AfterReceipt, token);

        var current = await scope.VerifyFreshnessAsync(scope.BeforeReceipt, scope.AfterReceipt, token);
        await Assert.That(current.Accepted).IsTrue();
        await Assert.That(current.Value.GetProperty(SiteGitHubEvidenceTokens.Fresh).GetBoolean()).IsTrue();
        await Assert.That(current.Value.GetProperty(SiteGitHubEvidenceTokens.Run)
            .GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
    }

    [Test]
    public async Task AC_BC_028_RejectsChangedSiteModeEligibilityAndMeasurementTuple()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        Action<JsonObject, SiteGitHubEvidenceScope>[] mutations =
        [
            (receipt, _) => receipt[SiteGitHubEvidenceTokens.SiteSourceRevision] = SiteGitHubEvidenceTokens.WrongRevision,
            (receipt, _) => receipt[SiteGitHubEvidenceTokens.ModeField] = SiteGitHubEvidenceTokens.ModeValidate,
            (receipt, _) => receipt[SiteGitHubEvidenceTokens.PublishEligible] = false,
            (receipt, _) => receipt[SiteGitHubEvidenceTokens.MeasuredRevision] = SiteGitHubEvidenceTokens.WrongRevision,
            (receipt, scope) => receipt[SiteGitHubEvidenceTokens.Run]![SiteGitHubEvidenceTokens.RunAttemptNested]
                = checked(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32() + SiteGitHubEvidenceTokens.One),
            ChangeJobIdentity,
            (receipt, scope) => receipt[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactId]
                = checked(scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64() + SiteGitHubEvidenceTokens.One),
            (receipt, scope) => receipt[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactDigest]
                = SiteGitHubJobArtifactOracle.DifferentDigest(scope.Expected.Artifact
                    .GetProperty(SiteGitHubEvidenceTokens.ArtifactDigest).GetString()!),
            (receipt, scope) => receipt[SiteGitHubEvidenceTokens.ComparisonJob]![SiteGitHubEvidenceTokens.Steps]![SiteGitHubEvidenceTokens.First]!
                [SiteGitHubEvidenceTokens.StepNumber] = SiteGitHubJobArtifactOracle.ExpectedStepNumber(scope.Expected,
                    SiteGitHubEvidenceTokens.First) + SiteTokens.One,
        ];
        foreach (var mutation in mutations)
        {
            await AssertChangedTupleAsync(mutation, token);
        }
    }

    private static async Task AssertChangedTupleAsync(Action<JsonObject, SiteGitHubEvidenceScope> mutation,
        CancellationToken token)
    {
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var before = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(before.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(before, scope.BeforeReceipt, token);
        var archive = await scope.VerifyArchiveAsync(scope.BeforeReceipt, token);
        await Assert.That(archive.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(archive, scope.BeforeReceipt, token);
        var after = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(after.Accepted).IsTrue();
        var changed = JsonNode.Parse(after.Value.GetRawText())!.AsObject();
        mutation(changed, scope);
        await File.WriteAllTextAsync(scope.AfterReceipt, changed.ToJsonString(), token);
        var current = await scope.VerifyFreshnessAsync(scope.BeforeReceipt, scope.AfterReceipt, token);
        await AssertError(current, SiteGitHubEvidenceTokens.ErrorFreshness);
    }

    private static void ChangeJobIdentity(JsonObject receipt, SiteGitHubEvidenceScope scope)
    {
        var job = receipt[SiteGitHubEvidenceTokens.ComparisonJob]!.AsObject();
        var previous = scope.Expected.Job.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var changed = checked(previous + SiteGitHubEvidenceTokens.One);
        job[SiteGitHubEvidenceTokens.ArtifactId] = changed;
        var url = job[SiteGitHubEvidenceTokens.ComparisonJobUrl]!.GetValue<string>();
        job[SiteGitHubEvidenceTokens.ComparisonJobUrl] = string.Concat(
            url.AsSpan(SiteGitHubEvidenceTokens.Zero, url.LastIndexOf(SiteTokens.UrlPathSeparatorCharacter) +
                SiteGitHubEvidenceTokens.One),
            changed.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Test]
    public async Task AC_BC_028_RejectsMetadataOnlyBeforeReceipt()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var metadata = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(metadata.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(metadata, scope.BeforeReceipt, token);
        await SiteGitHubEvidenceScope.WriteReceiptAsync(metadata, scope.AfterReceipt, token);
        await AssertError(await scope.VerifyFreshnessAsync(scope.BeforeReceipt, scope.AfterReceipt, token),
            SiteGitHubEvidenceTokens.ErrorFreshness);
    }

    [Test]
    public async Task AC_BC_028_RejectsStructurallyInvalidButMatchingFreshnessReceipts()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var proof = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(proof.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(proof, scope.BeforeReceipt, token);
        var verified = await scope.VerifyArchiveAsync(scope.BeforeReceipt, token);
        await Assert.That(verified.Accepted).IsTrue();
        Action<JsonObject, JsonObject>[] mutations =
        [
            (before, after) => MutateBoth(before, after, receipt =>
                receipt[SiteGitHubEvidenceTokens.ComparisonJob]![SiteGitHubEvidenceTokens.Steps] = new JsonArray()),
            (before, _) => before[SiteGitHubEvidenceTokens.MetadataFiles] = new JsonArray(),
            (before, after) => MutateBoth(before, after, receipt =>
                receipt[SiteGitHubEvidenceTokens.ComparisonJob]![SiteGitHubEvidenceTokens.StartedAt] = SiteGitHubEvidenceTokens.InvalidDate),
            (before, after) => MutateBoth(before, after, receipt =>
                receipt[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactCreatedAt] =
                    SiteGitHubJobArtifactOracle.BeforeJob(scope.Expected)),
            OversizeBoth,
        ];
        foreach (var mutation in mutations)
        {
            var before = JsonNode.Parse(verified.Value.GetRawText())!.AsObject();
            var after = JsonNode.Parse(proof.Value.GetRawText())!.AsObject();
            mutation(before, after);
            await File.WriteAllTextAsync(scope.BeforeReceipt, before.ToJsonString(), token);
            await File.WriteAllTextAsync(scope.AfterReceipt, after.ToJsonString(), token);
            await AssertError(await scope.VerifyFreshnessAsync(scope.BeforeReceipt, scope.AfterReceipt, token),
                SiteGitHubEvidenceTokens.ErrorFreshness);
        }
    }

    private static void MutateBoth(JsonObject before, JsonObject after, Action<JsonObject> mutation)
    {
        mutation(before);
        mutation(after);
    }

    private static void OversizeBoth(JsonObject before, JsonObject after)
    {
        var oversize = SiteGitHubEvidenceTokens.ArchiveBytes + SiteGitHubEvidenceTokens.One;
        before[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactSize] = oversize;
        after[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactSize] = oversize;
        before[SiteGitHubEvidenceTokens.Archive]![SiteGitHubEvidenceTokens.ArtifactBytes] = oversize;
    }

    private static async Task AssertError(SiteGitHubCliResult result, string expectedCode)
    {
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(expectedCode);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteTokens.Zero);
    }
}
