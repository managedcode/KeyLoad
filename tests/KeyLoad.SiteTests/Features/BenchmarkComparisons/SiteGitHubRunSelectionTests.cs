using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

/// <summary>Exercises bounded selection against retained authenticated run and exact-attempt captures.</summary>
internal sealed class SiteGitHubRunSelectionTests
{
    [Test]
    public async Task AC_BC_028_SelectsHighestSuccessfulComparisonAndAllowsValidatePin()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        var runId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64().ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        var pinned = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModeValidate, runId, token);
        await Assert.That(pinned.Accepted).IsTrue();
        await Assert.That(pinned.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.Selected);
        await Assert.That(pinned.Value.GetProperty(SiteGitHubEvidenceTokens.RunId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(pinned.Value.GetProperty(SiteGitHubEvidenceTokens.PublishEligible).GetBoolean()).IsFalse();
        await scope.PrepareControlledPublicationAsync(token);
        var latest = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(latest.Accepted).IsTrue();
        await Assert.That(latest.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.Selected);
        await Assert.That(latest.Value.GetProperty(SiteGitHubEvidenceTokens.RunId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(latest.Value.GetProperty(SiteGitHubEvidenceTokens.RunNumber).GetInt32())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunNumberField).GetInt32());
        await Assert.That(latest.Value.GetProperty(SiteGitHubEvidenceTokens.RunAttempt).GetInt32())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32());
        await Assert.That(latest.Value.GetProperty(SiteGitHubEvidenceTokens.PublishEligible).GetBoolean()).IsTrue();

        var forbiddenPin = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish,
            runId, token);
        await AssertError(forbiddenPin, SiteGitHubEvidenceTokens.ErrorArgument);
    }

    [Test]
    public async Task AC_BC_028_SelectsAcrossShuffledPaginatedAuthenticMetadata()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await scope.ShuffleAndPaginateAsync(SiteGitHubEvidenceTokens.RunsCapture,
            SiteGitHubEvidenceTokens.WorkflowRuns, token);
        await scope.ShuffleAndPaginateAsync(SiteGitHubEvidenceTokens.JobsCapture,
            SiteGitHubEvidenceTokens.Jobs, token);
        await scope.ShuffleAndPaginateAsync(SiteGitHubEvidenceTokens.ArtifactsCapture,
            SiteGitHubEvidenceTokens.Artifacts, token);
        var trail = SiteGitHubRunSelectionMutations.AttemptRelativePath(
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64(),
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32(),
            SiteGitHubEvidenceTokens.JobsCapture);
        await scope.ShuffleAndPaginateAsync(trail, SiteGitHubEvidenceTokens.Jobs, token);
        var selected = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(selected.Accepted).IsTrue();
        await Assert.That(selected.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
            .IsEqualTo(SiteGitHubEvidenceTokens.Selected);
        var proved = await scope.ProveAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(proved.Accepted).IsTrue();
        await SiteGitHubJobArtifactAssertions.AssertReceiptIdentity(proved.Value, scope);
        await SiteGitHubJobArtifactAssertions.AssertReceiptMetadataHashes(proved.Value, scope, token);
    }

    [Test]
    public async Task AC_BC_028_MissingAttemptRequestsPairAndPartialPairFailsClosed()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using (var missing = await SiteGitHubEvidenceScope.CreateAsync(token))
        {
            await missing.PrepareControlledPublicationAsync(token);
            var nextAttempt = await SiteGitHubRunSelectionMutations.PrepareMissingAttemptAsync(missing, token);
            var response = await missing.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
            await Assert.That(response.Accepted).IsTrue();
            await Assert.That(response.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
                .IsEqualTo(SiteGitHubEvidenceTokens.NeedsAttempt);
            await Assert.That(response.Value.GetProperty(SiteGitHubEvidenceTokens.RunId).GetInt64())
                .IsEqualTo(missing.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
            await Assert.That(response.Value.GetProperty(SiteGitHubEvidenceTokens.RunAttempt).GetInt32())
                .IsEqualTo(nextAttempt);
        }

        await using var partial = await SiteGitHubEvidenceScope.CreateAsync(token);
        await partial.PrepareControlledPublicationAsync(token);
        var partialAttempt = await SiteGitHubRunSelectionMutations.PrepareMissingAttemptAsync(partial, token);
        var partialRunPath = SiteGitHubRunSelectionMutations.AttemptRelativePath(partial.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId)
            .GetInt64(), partialAttempt, SiteGitHubEvidenceTokens.SelectedRunCapture);
        var partialRun = await partial.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.SelectedRunCapture, token);
        partialRun[SiteGitHubEvidenceTokens.RunAttemptField] = partialAttempt;
        await partial.WriteCaptureJsonAsync(partialRunPath, partialRun, token);
        var failed = await partial.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await AssertError(failed, SiteGitHubEvidenceTokens.ErrorCapture);
    }

    [Test]
    public async Task AC_BC_028_UsesEarlierSuccessfulAttemptAfterFailedRerun()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await SiteGitHubRunSelectionMutations.AddFailedRerunAsync(scope, token);

        var selected = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
        await Assert.That(selected.Accepted).IsTrue();
        await Assert.That(selected.Value.GetProperty(SiteGitHubEvidenceTokens.RunId).GetInt64())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64());
        await Assert.That(selected.Value.GetProperty(SiteGitHubEvidenceTokens.RunAttempt).GetInt32())
            .IsEqualTo(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32());
    }

    [Test]
    public async Task AC_BC_028_RejectsIncompleteAndDuplicateRunPagination()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using (var incomplete = await SiteGitHubEvidenceScope.CreateAsync(token))
        {
            await incomplete.PrepareControlledPublicationAsync(token);
            var pages = (JsonArray)await incomplete.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
            var page = pages[SiteGitHubEvidenceTokens.First]!.AsObject();
            page[SiteGitHubEvidenceTokens.TotalCount] = page[SiteGitHubEvidenceTokens.TotalCount]!.GetValue<int>() +
                SiteGitHubEvidenceTokens.One;
            await incomplete.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, pages, token);
            await AssertError(await incomplete.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token),
                SiteGitHubEvidenceTokens.ErrorPagination);
        }

        await using var duplicate = await SiteGitHubEvidenceScope.CreateAsync(token);
        await duplicate.PrepareControlledPublicationAsync(token);
        var duplicatePages = (JsonArray)await duplicate.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
        var duplicatePage = duplicatePages[SiteGitHubEvidenceTokens.First]!.AsObject();
        var runs = duplicatePage[SiteGitHubEvidenceTokens.WorkflowRuns]!.AsArray();
        runs.Add(runs[SiteGitHubEvidenceTokens.First]!.DeepClone());
        duplicatePage[SiteGitHubEvidenceTokens.TotalCount] = runs.Count;
        await duplicate.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, duplicatePages, token);
        await AssertError(await duplicate.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token),
            SiteGitHubEvidenceTokens.ErrorPagination);
    }

    [Test]
    public async Task AC_BC_028_RejectsWrongWorkflowAndRunIdentityFields()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using (var workflow = await SiteGitHubEvidenceScope.CreateAsync(token))
        {
            await workflow.PrepareControlledPublicationAsync(token);
            var capture = (JsonObject)await workflow.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.WorkflowCapture, token);
            capture[SiteGitHubEvidenceTokens.Path] = SiteGitHubEvidenceTokens.WrongWorkflowPath;
            await workflow.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.WorkflowCapture, capture, token);
            await AssertError(await workflow.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token),
                SiteGitHubEvidenceTokens.ErrorWorkflow);
        }

        Action<JsonObject, SiteGitHubEvidenceScope>[] invalidRuns =
        [
            (run, _) => run[SiteGitHubEvidenceTokens.Event] = SiteGitHubEvidenceTokens.WrongEvent,
            (run, _) => run[SiteGitHubEvidenceTokens.HeadBranch] = SiteGitHubEvidenceTokens.WrongBranch,
            (run, _) => run[SiteGitHubEvidenceTokens.HeadSha] = SiteGitHubEvidenceTokens.WrongRevision,
            (run, scope) => run[SiteGitHubEvidenceTokens.RunAttemptField] =
                scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32() + SiteGitHubEvidenceTokens.One,
            (run, _) => run[SiteGitHubEvidenceTokens.RepositoryObject]!.AsObject()[SiteGitHubEvidenceTokens.RepositoryFullNameField]
                = SiteGitHubEvidenceTokens.WrongRepository,
            (run, _) => run[SiteGitHubEvidenceTokens.HeadRepositoryObject]!.AsObject()[SiteGitHubEvidenceTokens.RepositoryFullNameField]
                = SiteGitHubEvidenceTokens.WrongRepository,
        ];

        foreach (var mutation in invalidRuns)
        {
            await AssertInvalidRunAsync(mutation, token);
        }
    }

    private static async Task AssertInvalidRunAsync(Action<JsonObject, SiteGitHubEvidenceScope> mutation,
        CancellationToken token)
    {
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        await SiteGitHubRunSelectionMutations.MutateCurrentAttemptAsync(scope, mutation, token);
        await AssertError(await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token),
            SiteGitHubEvidenceTokens.ErrorRun);
    }

    private static async Task AssertError(SiteGitHubCliResult result, string expectedCode)
    {
        await Assert.That(result.Accepted).IsFalse();
        await Assert.That(result.ExitCode != SiteTokens.ProcessSuccessExitCode).IsTrue();
        await Assert.That(result.ErrorCode).IsEqualTo(expectedCode);
        await Assert.That(result.StandardError.Length).IsEqualTo(SiteTokens.Zero);
    }
}

internal sealed class SiteGitHubRunSelectionBoundaryTests
{
    [Test]
    public async Task AC_BC_028_ReportsNoComparisonForPendingMissingOrSkippedJob()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        for (var scenario = SiteGitHubEvidenceTokens.Zero; scenario < SiteGitHubEvidenceTokens.JobUnavailableScenarios;
             scenario++)
        {
            await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
            await scope.PrepareControlledPublicationAsync(token);
            var relative = SiteGitHubRunSelectionMutations.AttemptRelativePath(
                scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64(),
                scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32(),
                SiteGitHubEvidenceTokens.JobsCapture);
            var pages = (JsonArray)await scope.ReadCaptureJsonAsync(relative, token);
            var job = pages.SelectMany(page => page![SiteGitHubEvidenceTokens.Jobs]!.AsArray())
                .Single(item => item![SiteTokens.Name]!.GetValue<string>() == SiteGitHubEvidenceTokens.ComparisonJobName)!;
            if (scenario == SiteGitHubEvidenceTokens.One)
            {
                var page = pages.Single(item => item![SiteGitHubEvidenceTokens.Jobs]!.AsArray().Contains(job))!;
                page[SiteGitHubEvidenceTokens.Jobs]!.AsArray().Remove(job);
                foreach (var item in pages)
                {
                    item![SiteGitHubEvidenceTokens.TotalCount] =
                        pages.Sum(part => part![SiteGitHubEvidenceTokens.Jobs]!.AsArray().Count);
                }
            }
            else if (scenario == SiteGitHubEvidenceTokens.Zero)
            {
                job[SiteGitHubEvidenceTokens.Status] = SiteGitHubEvidenceTokens.PendingStatus;
                job[SiteGitHubEvidenceTokens.Conclusion] = null;
            }
            else
            {
                job[SiteGitHubEvidenceTokens.Conclusion] = SiteGitHubEvidenceTokens.SkippedConclusion;
            }

            await scope.WriteCaptureJsonAsync(relative, pages, token);
            var result = await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token);
            await Assert.That(result.Accepted).IsTrue();
            await Assert.That(result.Value.GetProperty(SiteGitHubEvidenceTokens.State).GetString())
                .IsEqualTo(SiteGitHubEvidenceTokens.Unavailable);
            await Assert.That(result.Value.GetProperty(SiteGitHubEvidenceTokens.PublishEligible).GetBoolean()).IsFalse();
        }
    }

    [Test]
    public async Task AC_BC_028_RejectsMoreThanTheCompleteRunBound()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
        var original = pages[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.WorkflowRuns]!
            .AsArray()[SiteGitHubEvidenceTokens.First]!;
        var runs = new JsonArray();
        for (var index = SiteGitHubEvidenceTokens.One; index <= SiteGitHubEvidenceTokens.MaxRuns + SiteGitHubEvidenceTokens.One; index++)
        {
            var copy = original.DeepClone().AsObject();
            copy[SiteGitHubEvidenceTokens.ArtifactId] = index;
            copy[SiteGitHubEvidenceTokens.RunNumberField] = index;
            runs.Add(copy);
        }

        pages[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.WorkflowRuns] = runs;
        pages[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.TotalCount] = runs.Count;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, pages, token);
        await SiteGitHubJobArtifactAssertions.AssertError(await scope.SelectAsync(SiteGitHubEvidenceTokens.ModePublish, null, token),
            SiteGitHubEvidenceTokens.ErrorPagination);
    }

    [Test]
    public async Task AC_BC_028_RejectsMoreThanTheCompleteAttemptPairBound()
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var scope = await SiteGitHubEvidenceScope.CreateAsync(token);
        await scope.PrepareControlledPublicationAsync(token);
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
        var summary = pages[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.WorkflowRuns]!
            .AsArray()[SiteGitHubEvidenceTokens.First]!;
        summary[SiteGitHubEvidenceTokens.RunAttemptField] =
            SiteGitHubEvidenceTokens.MaxPairs + SiteGitHubEvidenceTokens.One;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, pages, token);

        var runId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var run = JsonNode.Parse(scope.Expected.Run.GetRawText())!.AsObject();
        var job = JsonNode.Parse(scope.Expected.Job.GetRawText())!.AsObject();
        job[SiteGitHubEvidenceTokens.Conclusion] = SiteGitHubEvidenceTokens.WrongConclusion;
        job[SiteGitHubEvidenceTokens.Steps] = new JsonArray();
        for (var attempt = SiteGitHubEvidenceTokens.MaxPairs + SiteGitHubEvidenceTokens.One;
             attempt > SiteGitHubEvidenceTokens.One; attempt--)
        {
            token.ThrowIfCancellationRequested();
            run[SiteGitHubEvidenceTokens.RunAttemptField] = attempt;
            job[SiteGitHubEvidenceTokens.RunAttemptField] = attempt;
            var runPath = scope.CaptureFile(SiteGitHubRunSelectionMutations.AttemptRelativePath(
                runId, attempt, SiteGitHubEvidenceTokens.SelectedRunCapture));
            var jobsPath = scope.CaptureFile(SiteGitHubRunSelectionMutations.AttemptRelativePath(
                runId, attempt, SiteGitHubEvidenceTokens.JobsCapture));
            Directory.CreateDirectory(Path.GetDirectoryName(runPath)!);
            await File.WriteAllTextAsync(runPath, run.ToJsonString(), token);
            var jobs = new JsonArray(new JsonObject
            {
                [SiteGitHubEvidenceTokens.TotalCount] = SiteGitHubEvidenceTokens.One,
                [SiteGitHubEvidenceTokens.Jobs] = new JsonArray(job.DeepClone()),
            });
            await File.WriteAllTextAsync(jobsPath, jobs.ToJsonString(), token);
        }

        await SiteGitHubJobArtifactAssertions.AssertError(await scope.SelectLongHistoryAsync(token),
            SiteGitHubEvidenceTokens.ErrorPagination);
    }

}

internal static class SiteGitHubRunSelectionMutations
{
    internal static async Task MutateCurrentAttemptAsync(SiteGitHubEvidenceScope scope,
        Action<JsonObject, SiteGitHubEvidenceScope> mutation, CancellationToken token)
    {
        var path = AttemptRelativePath(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64(),
            scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32(),
            SiteGitHubEvidenceTokens.SelectedRunCapture);
        var run = (JsonObject)await scope.ReadCaptureJsonAsync(path, token);
        mutation(run, scope);
        await scope.WriteCaptureJsonAsync(path, run, token);
    }

    internal static async Task AddFailedRerunAsync(SiteGitHubEvidenceScope scope, CancellationToken token)
    {
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
        var summaries = pages[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.WorkflowRuns]!.AsArray();
        var runId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var currentAttempt = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        var nextAttempt = checked(currentAttempt + SiteGitHubEvidenceTokens.One);
        var summary = summaries.Single(run => run![SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() == runId)!;
        summary[SiteGitHubEvidenceTokens.RunAttemptField] = nextAttempt;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, pages, token);

        var run = (JsonObject)await scope.ReadCaptureJsonAsync(AttemptRelativePath(
            runId, currentAttempt, SiteGitHubEvidenceTokens.SelectedRunCapture), token);
        run[SiteGitHubEvidenceTokens.RunAttemptField] = nextAttempt;
        var jobs = (JsonArray)await scope.ReadCaptureJsonAsync(AttemptRelativePath(
            runId, currentAttempt, SiteGitHubEvidenceTokens.JobsCapture), token);
        var rows = jobs[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.Jobs]!.AsArray();
        var job = rows.Single(row => row![SiteTokens.Name]!.GetValue<string>() ==
            SiteGitHubEvidenceTokens.ComparisonJobName)!.AsObject();
        job[SiteGitHubEvidenceTokens.ArtifactId] = checked(job[SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() + SiteGitHubEvidenceTokens.One);
        job[SiteGitHubEvidenceTokens.RunAttemptField] = nextAttempt;
        job[SiteGitHubEvidenceTokens.Conclusion] = SiteGitHubEvidenceTokens.WrongConclusion;
        var destination = AttemptDirectory(runId, nextAttempt);
        Directory.CreateDirectory(scope.CaptureFile(destination));
        await scope.WriteCaptureJsonAsync(string.Join(SiteTokens.UrlPathSeparator, destination,
            SiteGitHubEvidenceTokens.SelectedRunCapture), run, token);
        await scope.WriteCaptureJsonAsync(string.Join(SiteTokens.UrlPathSeparator, destination,
            SiteGitHubEvidenceTokens.JobsCapture), jobs, token);
    }

    internal static async Task<int> PrepareMissingAttemptAsync(SiteGitHubEvidenceScope scope, CancellationToken token)
    {
        var runId = scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var nextAttempt = checked(scope.Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32() +
            SiteGitHubEvidenceTokens.One);
        var pages = (JsonArray)await scope.ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
        var runs = pages[SiteGitHubEvidenceTokens.First]![SiteGitHubEvidenceTokens.WorkflowRuns]!.AsArray();
        var summary = runs.Single(run => run![SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() == runId)!.AsObject();
        summary[SiteGitHubEvidenceTokens.RunAttemptField] = nextAttempt;
        await scope.WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, pages, token);
        Directory.CreateDirectory(scope.CaptureFile(AttemptDirectory(runId, nextAttempt)));
        return nextAttempt;
    }

    internal static string AttemptRelativePath(long runId, int attempt, string file)
        => string.Join(SiteTokens.UrlPathSeparator,
            SiteGitHubEvidenceTokens.AttemptsDirectory,
            runId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            attempt.ToString(System.Globalization.CultureInfo.InvariantCulture), file);

    private static string AttemptDirectory(long runId, int attempt)
        => string.Join(SiteTokens.UrlPathSeparator, SiteGitHubEvidenceTokens.AttemptsDirectory,
            runId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
}
