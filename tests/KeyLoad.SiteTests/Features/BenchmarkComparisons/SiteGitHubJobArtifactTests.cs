using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Verifies only the exact successful comparison job and its immutable run artifact.</summary>
internal sealed class SiteGitHubJobArtifactTests
{
    [Test]
    public async Task AC_BC_028_ProvesExactComparisonJobAndArtifactWithoutGatingOnOtherJobs()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await SiteGitHubJobArtifactMutations.MutateTopLevelConclusionAndOtherJobAsync(scope, token);
        await scope.PrepareControlledPublicationAsync(token);
        var proof = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);

        await Assert.That(proof.Accepted).IsTrue();
        await Assert.That(proof.ExitCode).IsEqualTo(SiteTokens.ProcessSuccessExitCode);
        await Assert.That(proof.StandardError.Length).IsEqualTo(SiteTokens.Zero);
        var receipt = proof.Value;
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.MetadataVerified);
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.ModeField).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.ModePublish);
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.PublishEligible).GetBoolean()).IsTrue();
        await SiteGitHubJobArtifactAssertions.AssertReceiptIdentity(receipt, scope);
        await SiteGitHubJobArtifactAssertions.AssertReceiptMetadataHashes(receipt, scope, token);
    }

    [Test]
    public async Task AC_BC_028_RejectsSuccessfulJobsWithInvalidOrAmbiguousRequiredSteps()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        Action<JsonObject, SiteGitHubEvidenceScope>[] mutations =
        [
            (job, _) => SiteGitHubJobArtifactMutations.FindRequiredStep(job, SiteGitHubEvidenceTokens.SmallMeasurementStep)
                [SiteGitHubEvidenceTokens.Conclusion] = SiteGitHubEvidenceTokens.WrongConclusion,
            (job, _) => SiteGitHubJobArtifactMutations.RemoveRequiredStep(job, SiteGitHubEvidenceTokens.DotnetComparisonStep),
            (job, _) => job[SiteGitHubEvidenceTokens.Steps]!.AsArray().Add(
                SiteGitHubJobArtifactMutations.FindRequiredStep(job, SiteGitHubEvidenceTokens.DotnetComparisonStep).DeepClone()),
            (job, _) => job[SiteGitHubEvidenceTokens.RunAttemptField] =
                job[SiteGitHubEvidenceTokens.RunAttemptField]!.GetValue<int>() + SiteGitHubEvidenceTokens.One,
            (job, _) => job[SiteGitHubEvidenceTokens.HeadSha] = SiteGitHubEvidenceTokens.WrongRevision,
        ];

        foreach (var mutation in mutations)
        {
            await AssertInvalidJobAsync(mutation, token);
        }

        await using var duplicate = await SiteGitHubEvidenceScope.CreateAsync(token);
        await duplicate.PrepareControlledPublicationAsync(token);
        await SiteGitHubJobArtifactMutations.DuplicateSelectedJobAsync(duplicate, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await duplicate.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token),
            SiteGitHubEvidenceTokens.ErrorJob);
    }

    [Test]
    public async Task AC_BC_028_SelectedSuccessfulComparisonNeverFallsBackForBadArtifact()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        Action<JsonObject, SiteGitHubEvidenceScope>[] mutations =
        [
            (page, _) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.Expired] = true,
            (page, _) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.ArtifactDigest] =
                SiteGitHubEvidenceTokens.InvalidDigest,
            (page, _) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.SizeInBytes] = SiteGitHubEvidenceTokens.OversizedArtifact,
            (page, _) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.SizeInBytes] = SiteTokens.Zero,
            (page, _) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.CreatedAt] = SiteGitHubEvidenceTokens.InvalidDate,
            (page, scope) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.CreatedAt] =
                SiteGitHubJobArtifactOracle.BeforeJob(scope.Expected),
            (page, scope) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.CreatedAt] =
                SiteGitHubJobArtifactOracle.AfterJob(scope.Expected),
            (page, _) => SiteGitHubJobArtifactMutations.FindComparisonArtifact(page)[SiteGitHubEvidenceTokens.WorkflowRun]!.AsObject()
                [SiteGitHubEvidenceTokens.HeadSha] = SiteGitHubEvidenceTokens.WrongRevision,
            (page, _) => SiteGitHubJobArtifactMutations.RemoveSelectedArtifact(page),
            (page, _) => SiteGitHubJobArtifactMutations.DuplicateSelectedArtifact(page),
        ];

        foreach (var mutation in mutations)
        {
            await AssertInvalidArtifactAsync(mutation, token);
        }
    }

    private static async Task AssertInvalidJobAsync(Action<JsonObject, SiteGitHubEvidenceScope> mutation,
        CancellationToken token)
    {
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await SiteGitHubJobArtifactMutations.MutateSelectedJobAsync(scope, mutation, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.ProveAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorJob);
    }

    private static async Task AssertInvalidArtifactAsync(Action<JsonObject, SiteGitHubEvidenceScope> mutation,
        CancellationToken token)
    {
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await SiteGitHubJobArtifactMutations.MutateSelectedArtifactAsync(scope, mutation, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.ProveAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorArtifact);
    }

    [Test]
    public async Task AC_BC_028_RejectsDuplicateNonselectedJobAndArtifactIdentities()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using (var jobsScope = await SiteGitHubEvidenceScope.CreateAsync(token))
        {
            await jobsScope.PrepareControlledPublicationAsync(token);
            await jobsScope.ShuffleAndPaginateAsync(SiteGitHubEvidenceTokens.JobsCapture,
                SiteGitHubEvidenceTokens.Jobs, token);
            await SiteGitHubJobArtifactMutations.DuplicateNonselectedAsync(jobsScope,
                SiteGitHubEvidenceTokens.JobsCapture, SiteGitHubEvidenceTokens.Jobs,
                SiteGitHubEvidenceTokens.ComparisonJobName, token);
            await SiteGitHubJobArtifactAssertions.AssertError(await jobsScope.ProveAsync(
                SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorJob);
        }

        await using var artifactsScope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await artifactsScope.PrepareControlledPublicationAsync(token);
        await artifactsScope.ShuffleAndPaginateAsync(SiteGitHubEvidenceTokens.ArtifactsCapture,
            SiteGitHubEvidenceTokens.Artifacts, token);
        await SiteGitHubJobArtifactMutations.DuplicateNonselectedAsync(artifactsScope,
            SiteGitHubEvidenceTokens.ArtifactsCapture, SiteGitHubEvidenceTokens.Artifacts,
            SiteGitHubEvidenceTokens.ComparisonSuiteArtifact, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await artifactsScope.ProveAsync(
            SiteGitHubEvidenceTokens.ModePublish, null, token), SiteGitHubEvidenceTokens.ErrorArtifact);
    }

}

internal static class SiteGitHubJobArtifactAssertions
{
    internal static async Task AssertReceiptIdentity(System.Text.Json.JsonElement receipt,
        SiteGitHubEvidenceScope scope)
    {
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.SiteSourceRevision).GetString())
            .IsEqualTo(scope.SiteRevision);
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.ControlWorkflowRevision).GetString())
            .IsEqualTo(scope.WorkflowRevision);
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.MeasuredRevision).GetString())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.HeadSha).GetString());
        var run = receipt.GetProperty(SiteGitHubEvidenceTokens.Run);
        await Assert.That(run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(run.GetProperty(SiteGitHubEvidenceTokens.RunNumberReceipt).GetInt32())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunNumberField).GetInt32());
        await Assert.That(run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptNested).GetInt32())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32());
        await Assert.That(run.GetProperty(SiteGitHubEvidenceTokens.RunUrl).GetString())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.Url).GetString());
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.Repository)
            .GetProperty(SiteGitHubEvidenceTokens.FullName).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.RepositoryFullName);
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.Repository)
            .GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RepositoryObject)
                .GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.Workflow)
            .GetProperty(SiteGitHubEvidenceTokens.Path).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.WorkflowPath);
        await Assert.That(receipt.GetProperty(SiteGitHubEvidenceTokens.Workflow)
            .GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.WorkflowIdFieldRun).GetInt64());

        await AssertReceiptJobAndArtifact(receipt, scope);
    }

    private static async Task AssertReceiptJobAndArtifact(System.Text.Json.JsonElement receipt,
        SiteGitHubEvidenceScope scope)
    {
        var job = receipt.GetProperty(SiteGitHubEvidenceTokens.ComparisonJob);
        await Assert.That(job.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64())
            .IsEqualTo(scope.Expected.Job.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(job.GetProperty(SiteGitHubEvidenceTokens.ComparisonJobUrl).GetString())
            .IsEqualTo(scope.Expected.Job.GetProperty(SiteGitHubEvidenceTokens.Url).GetString());
        await Assert.That(job.GetProperty(SiteGitHubEvidenceTokens.StartedAt).GetString())
            .IsEqualTo(scope.Expected.Job.GetProperty(SiteGitHubEvidenceTokens.RunStartedAt).GetString());
        await Assert.That(job.GetProperty(SiteGitHubEvidenceTokens.CompletedAt).GetString())
            .IsEqualTo(scope.Expected.Job.GetProperty(SiteGitHubEvidenceTokens.RunCompletedAt).GetString());
        var steps = job.GetProperty(SiteGitHubEvidenceTokens.Steps);
        await Assert.That(steps.GetArrayLength()).IsEqualTo(SiteGitHubEvidenceTokens.ExpectedSteps);
        for (var index = SiteGitHubEvidenceTokens.First; index < SiteGitHubEvidenceTokens.ExpectedSteps; index++)
        {
            await Assert.That(steps[index].GetProperty(SiteGitHubEvidenceTokens.StepName).GetString())
                .IsEqualTo(SiteGitHubEvidenceTokens.MeasurementStepNames[index]);
            await Assert.That(steps[index].GetProperty(SiteGitHubEvidenceTokens.StepNumber).GetInt32())
                .IsEqualTo(SiteGitHubJobArtifactOracle.ExpectedStepNumber(scope.Expected, index));
        }

        var artifact = receipt.GetProperty(SiteGitHubEvidenceTokens.Artifact);
        await Assert.That(artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64())
            .IsEqualTo(scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactName).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.ComparisonSuiteArtifact);
        await Assert.That(artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactDigest).GetString())
            .IsEqualTo(scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactDigest).GetString());
        await Assert.That(artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactSize).GetInt64())
            .IsEqualTo(scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.SizeInBytes).GetInt64());
        await Assert.That(artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactCreatedAt).GetString())
            .IsEqualTo(scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.CreatedAt).GetString());
    }

    internal static async Task AssertReceiptMetadataHashes(System.Text.Json.JsonElement receipt,
        SiteGitHubEvidenceScope scope, CancellationToken token)
    {
        var files = receipt.GetProperty(SiteGitHubEvidenceTokens.MetadataFiles);
        var expectedPaths = await SiteGitHubExpectedEvidenceReader.ExpectedMetadataPathsAsync(
            scope.Capture, scope.Expected.Run, token);
        await Assert.That(files.GetArrayLength()).IsEqualTo(expectedPaths.Length);
        var paths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files.EnumerateArray())
        {
            var relative = file.GetProperty(SiteGitHubEvidenceTokens.FilePath).GetString()!;
            await Assert.That(paths.Add(relative)).IsTrue();
            var source = Path.Combine(scope.Capture, relative.Replace(SiteTokens.UrlPathSeparatorCharacter,
                Path.DirectorySeparatorChar));
            var bytes = await File.ReadAllBytesAsync(source, token);
            var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
            await Assert.That(file.GetProperty(SiteGitHubEvidenceTokens.FileSha256).GetString()).IsEqualTo(actual);
        }

        await Assert.That(paths.SetEquals(expectedPaths)).IsTrue();
    }

    internal static async Task AssertError(SiteGitHubCliResult result, string expectedCode)
    {
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(expectedCode);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteTokens.Zero);
    }
}

internal static class SiteGitHubJobArtifactMutations
{
    internal static async Task MutateTopLevelConclusionAndOtherJobAsync(SiteGitHubEvidenceScope scope,
        CancellationToken token)
    {
        var run = (JsonObject)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.SelectedRunCapture, token);
        run[SiteGitHubEvidenceTokens.Conclusion] = SiteGitHubEvidenceTokens.WrongConclusion;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.SelectedRunCapture, run, token);
        var jobs = (JsonArray)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.JobsCapture, token);
        var rows = jobs.SelectMany(page => page![SiteGitHubEvidenceTokens.Jobs]!.AsArray());
        var other = rows.FirstOrDefault(job => job![SiteTokens.Name]!.GetValue<string>() !=
            SiteGitHubEvidenceTokens.ComparisonJobName);
        other?[SiteGitHubEvidenceTokens.Conclusion] = SiteGitHubEvidenceTokens.WrongConclusion;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.JobsCapture, jobs, token);
    }

    internal static async Task MutateSelectedJobAsync(SiteGitHubEvidenceScope scope,
        Action<JsonObject, SiteGitHubEvidenceScope> mutation, CancellationToken token)
    {
        foreach (var path in SelectedJobPaths(scope))
        {
            var page = (JsonArray)await scope.ReadCaptureJsonAsync(path, token);
            mutation(FindComparisonJob(page, scope), scope);
            await scope.WriteCaptureJsonAsync(path, page, token);
        }
    }

    internal static async Task DuplicateSelectedJobAsync(SiteGitHubEvidenceScope scope, CancellationToken token)
    {
        foreach (var path in SelectedJobPaths(scope))
        {
            var page = (JsonArray)await scope.ReadCaptureJsonAsync(path, token);
            var selected = FindComparisonJob(page, scope);
            var jobs = page.Single(item => item![SiteGitHubEvidenceTokens.Jobs]!.AsArray()
                .Any(job => ReferenceEquals(job, selected)))![SiteGitHubEvidenceTokens.Jobs]!.AsArray();
            var duplicate = selected.DeepClone().AsObject();
            duplicate[SiteGitHubEvidenceTokens.ArtifactId] = checked(
                selected[SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() + SiteGitHubEvidenceTokens.One);
            jobs.Add(duplicate);
            SetTotalCount(page, SiteGitHubEvidenceTokens.Jobs);
            await scope.WriteCaptureJsonAsync(path, page, token);
        }
    }

    internal static async Task MutateSelectedArtifactAsync(SiteGitHubEvidenceScope scope,
        Action<JsonObject, SiteGitHubEvidenceScope> mutation, CancellationToken token)
    {
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.ArtifactsCapture, token);
        var artifactId = scope.Expected.Artifact.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var page = pages.Select(item => item!.AsObject()).Single(item => item[SiteGitHubEvidenceTokens.Artifacts]!
            .AsArray().Any(artifact => artifact![SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() == artifactId));
        mutation(page, scope);
        SetTotalCount(pages, SiteGitHubEvidenceTokens.Artifacts);
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.ArtifactsCapture, pages, token);
    }

    private static void SetTotalCount(JsonArray pages, string property)
    {
        var count = pages.Sum(page => page![property]!.AsArray().Count);
        foreach (var page in pages)
        {
            page![SiteGitHubEvidenceTokens.TotalCount] = count;
        }
    }

    internal static async Task DuplicateNonselectedAsync(SiteGitHubEvidenceScope scope, string file,
        string property, string selectedName, CancellationToken token)
    {
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(file, token);
        var rows = pages.SelectMany(page => page![property]!.AsArray()).Select(row => row!.AsObject()).ToArray();
        var nonselected = rows.FirstOrDefault(row => row[SiteTokens.Name]!.GetValue<string>() != selectedName);
        var duplicate = (nonselected ?? rows.Single()).DeepClone().AsObject();
        if (nonselected is null)
        {
            duplicate[SiteTokens.Name] = SiteGitHubEvidenceTokens.ControlledOtherName;
            duplicate[SiteGitHubEvidenceTokens.ArtifactId] = checked(
                duplicate[SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() + SiteGitHubEvidenceTokens.One);
            pages[SiteGitHubEvidenceTokens.First]![property]!.AsArray().Add(duplicate.DeepClone());
        }

        pages[^SiteGitHubEvidenceTokens.One]![property]!.AsArray().Add(duplicate);
        SetTotalCount(pages, property);
        await scope.WriteCaptureJsonAsync(file, pages, token);
    }

    private static JsonObject FindComparisonJob(JsonArray pages, SiteGitHubEvidenceScope scope)
        => pages.SelectMany(page => page![SiteGitHubEvidenceTokens.Jobs]!.AsArray())
            .Select(job => job!.AsObject()).Single(job =>
                job[SiteTokens.Name]!.GetValue<string>() == SiteGitHubEvidenceTokens.ComparisonJobName &&
                job[SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() ==
                    scope.Expected.Job.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());

    internal static JsonObject FindComparisonArtifact(JsonObject page)
        => FindComparisonArtifact(page[SiteGitHubEvidenceTokens.Artifacts]!.AsArray());

    private static JsonObject FindComparisonArtifact(JsonArray artifacts)
        => artifacts.Select(artifact => artifact!.AsObject())
            .Single(artifact => artifact[SiteTokens.Name]!.GetValue<string>() == SiteGitHubEvidenceTokens.ComparisonSuiteArtifact);

    internal static JsonObject FindRequiredStep(JsonObject job, string name)
        => job[SiteGitHubEvidenceTokens.Steps]!.AsArray().Select(step => step!.AsObject())
            .Single(step => step[SiteTokens.Name]!.GetValue<string>() == name);

    internal static void RemoveRequiredStep(JsonObject job, string name)
        => job[SiteGitHubEvidenceTokens.Steps]!.AsArray().Remove(FindRequiredStep(job, name));

    private static string[] SelectedJobPaths(SiteGitHubEvidenceScope scope)
    {
        var trail = string.Join(SiteTokens.UrlPathSeparator, SiteGitHubEvidenceTokens.AttemptsDirectory,
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture),
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture),
            SiteGitHubEvidenceTokens.JobsCapture);
        return [SiteGitHubEvidenceTokens.JobsCapture, trail];
    }

    internal static void RemoveSelectedArtifact(JsonObject page)
    {
        var artifacts = page[SiteGitHubEvidenceTokens.Artifacts]!.AsArray();
        artifacts.Remove(FindComparisonArtifact(artifacts));
    }

    internal static void DuplicateSelectedArtifact(JsonObject page)
    {
        var artifacts = page[SiteGitHubEvidenceTokens.Artifacts]!.AsArray();
        artifacts.Add(FindComparisonArtifact(artifacts).DeepClone());
    }

}

internal static class SiteGitHubJobArtifactOracle
{
    private static DateTimeOffset JobTime(SiteGitHubExpectedEvidence expected, string property)
        => DateTimeOffset.Parse(expected.Job.GetProperty(property).GetString()!,
            System.Globalization.CultureInfo.InvariantCulture);

    internal static string DifferentDigest(string digest)
    {
        var characters = digest.ToCharArray();
        var lastIndex = characters.Length - SiteTokens.One;
        characters[lastIndex] = characters[lastIndex] == SiteGitHubEvidenceTokens.DigestZero
            ? SiteGitHubEvidenceTokens.DigestOne : SiteGitHubEvidenceTokens.DigestZero;
        return new string(characters);
    }

    internal static int ExpectedStepNumber(SiteGitHubExpectedEvidence expected, int index)
    {
        var name = SiteGitHubEvidenceTokens.MeasurementStepNames[index];
        return expected.Job.GetProperty(SiteGitHubEvidenceTokens.Steps).EnumerateArray()
            .Single(step => step.GetProperty(SiteTokens.Name).GetString() == name)
            .GetProperty(SiteGitHubEvidenceTokens.StepNumber).GetInt32();
    }

    internal static string BeforeJob(SiteGitHubExpectedEvidence expected)
        => JobTime(expected, SiteGitHubEvidenceTokens.RunStartedAt).AddDays(-SiteTokens.One)
            .ToString(SiteGitHubEvidenceTokens.RoundTripDateFormat, System.Globalization.CultureInfo.InvariantCulture);

    internal static string AfterJob(SiteGitHubExpectedEvidence expected)
        => JobTime(expected, SiteGitHubEvidenceTokens.RunCompletedAt).AddDays(SiteTokens.One)
            .ToString(SiteGitHubEvidenceTokens.RoundTripDateFormat, System.Globalization.CultureInfo.InvariantCulture);

}
