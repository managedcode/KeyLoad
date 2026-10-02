using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Checks retained bytes against the authentic run's recorded artifact digest and size.</summary>
internal sealed class SiteGitHubEvidenceDigestTests
{
    [Test]
    public async Task AC_BC_028_VerifiesAuthenticArchiveLengthAndDigestFromMetadataReceipt()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var proof = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(proof.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(proof, scope.BeforeReceipt, token);

        var verified = await scope.VerifyArchiveAsync(scope.BeforeReceipt, token);
        await Assert.That(verified.Accepted).IsTrue();
        await Assert.That(verified.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.ArchiveVerified);
        var archive = verified.Value.GetProperty(SiteGitHubEvidenceTokens.Archive);
        await Assert.That(archive.GetProperty(SiteGitHubEvidenceTokens.ArtifactSha256).GetString())
            .IsEqualTo(scope.Expected.ArchiveSha256);
        await Assert.That(archive.GetProperty(SiteGitHubEvidenceTokens.ArtifactBytes).GetInt64())
            .IsEqualTo(scope.Expected.ArchiveBytes);
    }

    [Test]
    public async Task AC_BC_028_PinnedHistoricalProofHashesEveryVisitedAttemptPair()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await SiteGitHubRunSelectionMutations.AddFailedRerunAsync(scope, token);
        var runId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId)
            .GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var proof = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModeValidate, runId, token);
        await Assert.That(proof.Accepted).IsTrue();
        await Assert.That(proof.Value.GetProperty(SiteGitHubEvidenceTokens.PublishEligible).GetBoolean()).IsFalse();
        await SiteGitHubJobArtifactAssertions.AssertReceiptIdentity(proof.Value, scope);
        await SiteGitHubJobArtifactAssertions.AssertReceiptMetadataHashes(proof.Value, scope, token);
    }

    [Test]
    public async Task AC_BC_028_RejectsArchiveByteAndReceiptIdentityMismatches()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var proof = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(proof.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(proof, scope.BeforeReceipt, token);
        var original = await File.ReadAllBytesAsync(scope.Archive, token);
        var corrupted = (byte[])original.Clone();
        corrupted[^SiteTokens.One] ^= SiteGitHubEvidenceTokens.ByteToggle;
        await File.WriteAllBytesAsync(scope.Archive, corrupted, token);
        await AssertError(await scope.VerifyArchiveAsync(scope.BeforeReceipt, token),
            SiteGitHubEvidenceTokens.ErrorArchive);
        await File.WriteAllBytesAsync(scope.Archive, original, token);

        var receipt = await File.ReadAllTextAsync(scope.BeforeReceipt, token);
        var changed = JsonNode.Parse(receipt)!.AsObject();
        changed[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactDigest]
            = SiteGitHubJobArtifactOracle.DifferentDigest(
                scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactDigest).GetString()!);
        await File.WriteAllTextAsync(scope.AfterReceipt, changed.ToJsonString(), token);
        await AssertError(await scope.VerifyArchiveAsync(scope.AfterReceipt, token),
            SiteGitHubEvidenceTokens.ErrorArchive);
    }

    private static async Task AssertError(SiteGitHubCliResult result, string expectedCode)
    {
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(expectedCode);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteTokens.Zero);
    }
}

internal sealed class SiteGitHubReceiptShapeTests
{
    [Test]
    public async Task AC_BC_028_RejectsMalformedStepAndMetadataReceiptsBeforeArchiveProof()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var proof = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(proof.Accepted).IsTrue();
        Action<JsonObject>[] mutations =
        [
            receipt => Steps(receipt).Clear(),
            receipt => Steps(receipt).RemoveAt(SiteGitHubEvidenceTokens.First),
            receipt => Steps(receipt).Add(Steps(receipt)[SiteGitHubEvidenceTokens.First]!.DeepClone()),
            receipt => Steps(receipt)[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.StepNumber] = SiteGitHubEvidenceTokens.Zero,
            receipt => Steps(receipt)[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.StepName] = SiteGitHubEvidenceTokens.InvalidStepName,
            receipt => Metadata(receipt).Clear(),
            receipt => Metadata(receipt).Add(Metadata(receipt)[SiteGitHubEvidenceTokens.First]!.DeepClone()),
            receipt => Metadata(receipt)[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.FilePath] = SiteGitHubEvidenceTokens.NoncanonicalMetadataPath,
            receipt => Metadata(receipt)[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.FilePath] = SiteGitHubEvidenceTokens.TraversalMetadataPath,
            receipt => Metadata(receipt)[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.FileSha256] = SiteGitHubEvidenceTokens.WrongRevision,
            receipt => RemoveMetadata(receipt, SiteGitHubEvidenceTokens.WorkflowCapture),
            receipt => RemoveMetadata(receipt, SiteGitHubEvidenceTokens.RunsCapture),
            receipt => RemoveMetadata(receipt, SiteGitHubEvidenceTokens.SelectedRunCapture),
            receipt => RemoveMetadata(receipt, SiteGitHubEvidenceTokens.JobsCapture),
            receipt => RemoveMetadata(receipt, SiteGitHubEvidenceTokens.ArtifactsCapture),
            receipt => RemoveSelectedAttemptMetadata(receipt, scope, SiteGitHubEvidenceTokens.SelectedRunCapture),
            receipt => RemoveSelectedAttemptMetadata(receipt, scope, SiteGitHubEvidenceTokens.JobsCapture),
            receipt => AddUnmatchedMetadataPair(receipt, scope),
            receipt => AddTooManyCompleteAttemptPairs(receipt, scope),
            receipt => receipt[SiteGitHubEvidenceTokens.ComparisonJob]![SiteGitHubEvidenceTokens.StartedAt] = SiteGitHubEvidenceTokens.InvalidDate,
            receipt => receipt[SiteGitHubEvidenceTokens.ComparisonJob]![SiteGitHubEvidenceTokens.CompletedAt] =
                SiteGitHubJobArtifactOracle.BeforeJob(scope.Expected),
            receipt => receipt[SiteGitHubEvidenceTokens.Artifact]![SiteGitHubEvidenceTokens.ArtifactCreatedAt] =
                SiteGitHubJobArtifactOracle.BeforeJob(scope.Expected),
        ];
        foreach (var mutation in mutations)
        {
            var changed = JsonNode.Parse(proof.Value.GetRawText())!.AsObject();
            mutation(changed);
            await File.WriteAllTextAsync(scope.AfterReceipt, changed.ToJsonString(), token);
            await SiteGitHubJobArtifactAssertions.AssertError(await scope.VerifyArchiveAsync(scope.AfterReceipt, token),
                SiteGitHubEvidenceTokens.ErrorArchive);
        }
    }

    private static JsonArray Steps(JsonObject receipt)
        => receipt[SiteGitHubEvidenceTokens.ComparisonJob]![SiteGitHubEvidenceTokens.Steps]!.AsArray();

    private static JsonArray Metadata(JsonObject receipt)
        => receipt[SiteGitHubEvidenceTokens.MetadataFiles]!.AsArray();

    private static void RemoveMetadata(JsonObject receipt, string path)
    {
        var files = Metadata(receipt);
        files.Remove(files.Single(item => item![SiteGitHubEvidenceTokens.FilePath]!.GetValue<string>() == path));
    }

    private static void RemoveSelectedAttemptMetadata(JsonObject receipt, SiteGitHubEvidenceScope scope, string file)
    {
        var path = SiteGitHubRunSelectionMutations.AttemptRelativePath(
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64(),
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32(),
            file);
        RemoveMetadata(receipt, path);
    }

    private static void AddUnmatchedMetadataPair(JsonObject receipt, SiteGitHubEvidenceScope scope)
    {
        var runId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var selectedAttempt = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        var file = SiteGitHubEvidenceTokens.SelectedRunCapture;
        var existingPath = SiteGitHubRunSelectionMutations.AttemptRelativePath(runId, selectedAttempt, file);
        var extraPath = SiteGitHubRunSelectionMutations.AttemptRelativePath(runId,
            checked(selectedAttempt + SiteGitHubEvidenceTokens.One), file);
        var metadata = Metadata(receipt);
        var extra = metadata.Single(item => item![SiteGitHubEvidenceTokens.FilePath]!
            .GetValue<string>() == existingPath)!.DeepClone().AsObject();
        extra[SiteGitHubEvidenceTokens.FilePath] = extraPath;
        metadata.Add(extra);
    }

    private static void AddTooManyCompleteAttemptPairs(JsonObject receipt, SiteGitHubEvidenceScope scope)
    {
        var selectedId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var selectedAttempt = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        var files = Metadata(receipt);
        var runPath = SiteGitHubRunSelectionMutations.AttemptRelativePath(selectedId, selectedAttempt,
            SiteGitHubEvidenceTokens.SelectedRunCapture);
        var jobsPath = SiteGitHubRunSelectionMutations.AttemptRelativePath(selectedId, selectedAttempt,
            SiteGitHubEvidenceTokens.JobsCapture);
        var runTemplate = files.Single(file => file![SiteGitHubEvidenceTokens.FilePath]!.GetValue<string>() == runPath)!;
        var jobsTemplate = files.Single(file => file![SiteGitHubEvidenceTokens.FilePath]!.GetValue<string>() == jobsPath)!;
        for (var offset = SiteGitHubEvidenceTokens.One; offset <= SiteGitHubEvidenceTokens.MaxPairs; offset++)
        {
            var runId = selectedId > SiteGitHubEvidenceTokens.MaxPairs ? selectedId - offset : selectedId + offset;
            var run = runTemplate.DeepClone().AsObject();
            run[SiteGitHubEvidenceTokens.FilePath] = SiteGitHubRunSelectionMutations.AttemptRelativePath(
                runId, SiteGitHubEvidenceTokens.One, SiteGitHubEvidenceTokens.SelectedRunCapture);
            var jobs = jobsTemplate.DeepClone().AsObject();
            jobs[SiteGitHubEvidenceTokens.FilePath] = SiteGitHubRunSelectionMutations.AttemptRelativePath(
                runId, SiteGitHubEvidenceTokens.One, SiteGitHubEvidenceTokens.JobsCapture);
            files.Add(run);
            files.Add(jobs);
        }
    }
}

internal sealed class SiteGitHubCliBoundaryTests
{
    [Test]
    public async Task AC_BC_028_RejectsInvalidArgumentsAndSourceRevisions()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.SelectAsync(
            SiteGitHubEvidenceTokens.InvalidMode, null, token), SiteGitHubEvidenceTokens.ErrorArgument);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.SelectAsync(
            SiteGitHubEvidenceTokens.ModeValidate, SiteGitHubEvidenceTokens.InvalidSourceRevision, token),
            SiteGitHubEvidenceTokens.ErrorArgument);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.RunCliAsync(
            SiteGitHubEvidenceTokens.CommandSelect,
            [SiteGitHubEvidenceTokens.ArgumentInput + SiteGitHubEvidenceTokens.RelativeCapture,
             SiteGitHubEvidenceTokens.ArgumentMode + SiteGitHubEvidenceTokens.ModePublish], token),
            SiteGitHubEvidenceTokens.ErrorArgument);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.RunCliAsync(
            SiteGitHubEvidenceTokens.CommandSelect,
            [SiteGitHubEvidenceTokens.ArgumentInput + scope.Capture,
             SiteGitHubEvidenceTokens.ArgumentMode + SiteGitHubEvidenceTokens.ModePublish,
             SiteGitHubEvidenceTokens.UnknownArgument], token), SiteGitHubEvidenceTokens.ErrorArgument);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.RunCliAsync(
            SiteGitHubEvidenceTokens.CommandProve,
            [SiteGitHubEvidenceTokens.ArgumentInput + scope.Capture,
             SiteGitHubEvidenceTokens.ArgumentMode + SiteGitHubEvidenceTokens.ModePublish,
             SiteGitHubEvidenceTokens.ArgumentSiteRevision + SiteGitHubEvidenceTokens.InvalidSourceRevision,
             SiteGitHubEvidenceTokens.ArgumentWorkflowRevision + scope.WorkflowRevision], token),
            SiteGitHubEvidenceTokens.ErrorSource);
    }

    [Test]
    public async Task AC_BC_028_RejectsAbsentOrOversizedCaptureAndArchiveInputs()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var missing = await SiteGitHubEvidenceScope.CreateAsync(token);
        await missing.PrepareControlledPublicationAsync(token);
        File.Delete(missing.CaptureFile(SiteGitHubEvidenceTokens.WorkflowCapture));
        await SiteGitHubJobArtifactAssertions.AssertError(await missing.SelectAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorCapture);

        await using var missingSelected = await SiteGitHubEvidenceScope.CreateAsync(token);
        await missingSelected.PrepareControlledPublicationAsync(token);
        File.Delete(missingSelected.CaptureFile(SiteGitHubEvidenceTokens.SelectedRunCapture));
        await SiteGitHubJobArtifactAssertions.AssertError(await missingSelected.ProveAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorCapture);

        await using var oversized = await SiteGitHubEvidenceScope.CreateAsync(token);
        await oversized.PrepareControlledPublicationAsync(token);
        using (var stream = new FileStream(oversized.CaptureFile(SiteGitHubEvidenceTokens.WorkflowCapture),
                   FileMode.Open, FileAccess.Write))
        {
            stream.SetLength((long)SiteGitHubEvidenceTokens.MetadataBytes + SiteGitHubEvidenceTokens.One);
        }

        await SiteGitHubJobArtifactAssertions.AssertError(await oversized.SelectAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorCapture);

        await using var archiveScope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await archiveScope.PrepareControlledPublicationAsync(token);
        var proof = await archiveScope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(proof.Accepted).IsTrue();
        await SiteGitHubEvidenceScope.WriteReceiptAsync(proof, archiveScope.BeforeReceipt, token);
        using (var stream = new FileStream(archiveScope.Archive, FileMode.Open, FileAccess.Write))
        {
            stream.SetLength((long)SiteGitHubEvidenceTokens.ArchiveBytes + SiteGitHubEvidenceTokens.One);
        }

        await SiteGitHubJobArtifactAssertions.AssertError(await archiveScope.VerifyArchiveAsync(
            archiveScope.BeforeReceipt, token), SiteGitHubEvidenceTokens.ErrorArchive);
    }
}

internal sealed class SiteGitHubInventoryIdentityTests
{
    [Test]
    public async Task AC_BC_028_RejectsDuplicateNumbersAcrossRequiredMeasurementSteps()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var path = SiteGitHubRunSelectionMutations.AttemptRelativePath(
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64(),
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32(),
            SiteGitHubEvidenceTokens.JobsCapture);
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(path, token);
        var job = pages.SelectMany(page => page![SiteGitHubEvidenceTokens.Jobs]!.AsArray())
            .Select(item => item!.AsObject()).Single(item =>
                item[SiteTokens.Name]!.GetValue<string>() == SiteGitHubEvidenceTokens.ComparisonJobName);
        var first = SiteGitHubJobArtifactMutations.FindRequiredStep(job,
            SiteGitHubEvidenceTokens.DotnetComparisonStep);
        var second = SiteGitHubJobArtifactMutations.FindRequiredStep(job,
            SiteGitHubEvidenceTokens.SmallMeasurementStep);
        second[SiteGitHubEvidenceTokens.StepNumber] =
            first[SiteGitHubEvidenceTokens.StepNumber]!.GetValue<int>();
        await scope.WriteCaptureJsonAsync(path, pages, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.SelectAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorJob);
    }

    [Test]
    public async Task AC_BC_028_RejectsZeroAndMissingNonselectedInventoryIds()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        foreach (var remove in new[] { false, true })
        {
            await AssertInvalidNonselectedAsync(SiteGitHubEvidenceTokens.JobsCapture,
                SiteGitHubEvidenceTokens.Jobs, SiteGitHubEvidenceTokens.ComparisonJobName,
                SiteGitHubEvidenceTokens.ErrorJob, remove, token);
            await AssertInvalidNonselectedAsync(SiteGitHubEvidenceTokens.ArtifactsCapture,
                SiteGitHubEvidenceTokens.Artifacts, SiteGitHubEvidenceTokens.ComparisonSuiteArtifact,
                SiteGitHubEvidenceTokens.ErrorArtifact, remove, token);
        }
    }

    private static async Task AssertInvalidNonselectedAsync(string file, string property, string selectedName,
        string expectedCode, bool remove, CancellationToken token)
    {
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(file, token);
        var rows = pages.SelectMany(page => page![property]!.AsArray())
            .Select(row => row!.AsObject()).ToArray();
        var other = rows.FirstOrDefault(row => row[SiteTokens.Name]!.GetValue<string>() != selectedName);
        if (other is null)
        {
            other = rows.Single().DeepClone().AsObject();
            other[SiteTokens.Name] = SiteGitHubEvidenceTokens.ControlledOtherName;
            pages[^SiteGitHubEvidenceTokens.One]![property]!.AsArray().Add(other);
        }

        if (remove)
        {
            other.Remove(SiteGitHubEvidenceTokens.ArtifactId);
        }
        else
        {
            other[SiteGitHubEvidenceTokens.ArtifactId] = SiteGitHubEvidenceTokens.Zero;
        }
        var count = pages.Sum(page => page![property]!.AsArray().Count);
        foreach (var page in pages)
        {
            page![SiteGitHubEvidenceTokens.TotalCount] = count;
        }

        await scope.WriteCaptureJsonAsync(file, pages, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.ProveAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), expectedCode);
    }
}
