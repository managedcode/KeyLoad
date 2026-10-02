using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal sealed record SiteGitHubCliResult(int ExitCode, bool Accepted, JsonElement Value,
    string? ErrorCode, string? ErrorMessage, string StandardOutput, string StandardError);

internal sealed class SiteGitHubEvidenceScope : IAsyncDisposable
{
    private SiteTempDirectory? _temporary;
    private readonly string _repository;

    private SiteGitHubEvidenceScope(string repository, string siteRevision, string workflowRevision)
    {
        _repository = repository;
        SiteRevision = siteRevision;
        WorkflowRevision = workflowRevision;
    }

    internal string Capture => Path.Combine(RequiredTemporary.Path, SiteGitHubEvidenceTokens.CaptureDirectory);
    internal string Archive => Path.Combine(RequiredTemporary.Path, SiteGitHubEvidenceTokens.ArchiveFile);
    internal string BeforeReceipt => Path.Combine(RequiredTemporary.Path, SiteGitHubEvidenceTokens.BeforeReceiptFile);
    internal string AfterReceipt => Path.Combine(RequiredTemporary.Path, SiteGitHubEvidenceTokens.AfterReceiptFile);
    internal string SiteRevision { get; }
    internal string WorkflowRevision { get; }
    internal SiteGitHubExpectedEvidence Expected { get; private set; } = null!;

    internal static async Task<SiteGitHubEvidenceScope> CreateAsync(CancellationToken token)
    {
        var repository = SiteGitHubEvidenceInputs.RequiredAbsoluteEnvironment(SiteTokens.RepositoryEnvironment);
        var captureSource = SiteGitHubEvidenceInputs.RequiredAbsoluteEnvironment(SiteGitHubEvidenceTokens.CaptureEnvironment);
        var archiveSource = SiteGitHubEvidenceInputs.RequiredAbsoluteEnvironment(SiteGitHubEvidenceTokens.ArchiveEnvironment);
        var siteRevision = SiteGitHubEvidenceInputs.RequiredRevisionEnvironment(SiteGitHubEvidenceTokens.SiteRevisionEnvironment);
        var workflowRevision = SiteGitHubEvidenceInputs.RequiredRevisionEnvironment(SiteGitHubEvidenceTokens.WorkflowRevisionEnvironment);
        if (!Directory.Exists(repository) || !Directory.Exists(captureSource) || !File.Exists(archiveSource))
        {
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CaptureMissing);
        }

        var scope = new SiteGitHubEvidenceScope(repository, siteRevision, workflowRevision);
        try
        {
            scope.CreateTemporary();
            await SiteGitHubEvidenceInputs.CopyDirectoryAsync(captureSource, scope.Capture, token);
            await File.WriteAllBytesAsync(scope.Archive, await File.ReadAllBytesAsync(archiveSource, token), token);
            scope.Expected = await SiteGitHubExpectedEvidenceReader.ReadAsync(scope.Capture, scope.Archive, token);
            return scope;
        }
        catch (Exception)
        {
            await scope.DisposeAsync();
            throw;
        }
    }

    internal Task<SiteGitHubCliResult> SelectAsync(string mode, string? requestedRun, CancellationToken token)
        => RunAsync(SiteGitHubEvidenceTokens.CommandSelect, InputAndMode(mode, requestedRun), token);

    internal Task<SiteGitHubCliResult> SelectLongHistoryAsync(CancellationToken token)
        => RunAsync(SiteGitHubEvidenceTokens.CommandSelect,
            InputAndMode(SiteGitHubEvidenceTokens.ModePublish, null), token,
            SiteGitHubEvidenceTokens.HistoryTimeoutMilliseconds);

    internal Task<SiteGitHubCliResult> RunCliAsync(string command, string[] arguments, CancellationToken token)
        => RunAsync(command, arguments, token);

    // A controlled history projection keeps the authentic selected run, jobs and ZIP bytes.
    // It is rejection/selection test data, never a claim about the live latest run.
    internal async Task PrepareControlledPublicationAsync(CancellationToken token)
    {
        var pages = (JsonArray)await ReadCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture, token);
        var selectedId = Expected.Run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var selected = pages.SelectMany(page => page![SiteGitHubEvidenceTokens.WorkflowRuns]!.AsArray())
            .Single(run => run![SiteGitHubEvidenceTokens.ArtifactId]!.GetValue<long>() == selectedId)!
            .DeepClone().AsObject();
        selected[SiteGitHubEvidenceTokens.RunAttemptField] =
            Expected.Run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        await WriteCaptureJsonAsync(SiteGitHubEvidenceTokens.RunsCapture,
            new JsonArray(new JsonObject
            {
                [SiteGitHubEvidenceTokens.TotalCount] = SiteGitHubEvidenceTokens.One,
                [SiteGitHubEvidenceTokens.WorkflowRuns] = new JsonArray(selected),
            }), token);
    }

    internal async Task ShuffleAndPaginateAsync(string relativePath, string property, CancellationToken token)
    {
        var pages = (JsonArray)await ReadCaptureJsonAsync(relativePath, token);
        var rows = pages.SelectMany(page => page![property]!.AsArray())
            .Select(row => row!.DeepClone()).Reverse().ToArray();
        var midpoint = rows.Length / 2;
        var first = new JsonArray(rows.Take(midpoint).ToArray());
        var second = new JsonArray(rows.Skip(midpoint).ToArray());
        var divided = new JsonArray(
            new JsonObject { [SiteGitHubEvidenceTokens.TotalCount] = rows.Length, [property] = first },
            new JsonObject { [SiteGitHubEvidenceTokens.TotalCount] = rows.Length, [property] = second });
        await WriteCaptureJsonAsync(relativePath, divided, token);
    }

    internal Task<SiteGitHubCliResult> ProveAsync(string mode, string? requestedRun, CancellationToken token)
    {
        var arguments = InputAndMode(mode, requestedRun).ToList();
        arguments.Add(SiteGitHubEvidenceTokens.ArgumentSiteRevision + SiteRevision);
        arguments.Add(SiteGitHubEvidenceTokens.ArgumentWorkflowRevision + WorkflowRevision);
        return RunAsync(SiteGitHubEvidenceTokens.CommandProve, [.. arguments], token);
    }

    internal Task<SiteGitHubCliResult> VerifyArchiveAsync(string receipt, CancellationToken token)
        => RunAsync(SiteGitHubEvidenceTokens.CommandVerifyArchive,
            [SiteGitHubEvidenceTokens.ArgumentReceipt + receipt,
             SiteGitHubEvidenceTokens.ArgumentArchive + Archive], token);

    internal Task<SiteGitHubCliResult> VerifyFreshnessAsync(string before, string after, CancellationToken token)
        => RunAsync(SiteGitHubEvidenceTokens.CommandFresh,
            [SiteGitHubEvidenceTokens.ArgumentBefore + before, SiteGitHubEvidenceTokens.ArgumentAfter + after], token);

    internal string CaptureFile(params string[] parts) => Path.Combine([Capture, .. parts]);

    internal async Task<JsonNode> ReadCaptureJsonAsync(string relativePath, CancellationToken token)
    {
        var path = Path.Combine(Capture, relativePath.Replace(SiteTokens.UrlPathSeparatorCharacter,
            Path.DirectorySeparatorChar));
        return JsonNode.Parse(await File.ReadAllBytesAsync(path, token)) ??
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CaptureMissing);
    }

    internal async Task WriteCaptureJsonAsync(string relativePath, JsonNode value, CancellationToken token)
    {
        var path = Path.Combine(Capture, relativePath.Replace(SiteTokens.UrlPathSeparatorCharacter,
            Path.DirectorySeparatorChar));
        await File.WriteAllTextAsync(path, value.ToJsonString(), token);
    }

    internal static async Task<string> WriteReceiptAsync(SiteGitHubCliResult result, string path,
        CancellationToken token)
    {
        if (!result.Accepted)
        {
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CliFailure);
        }

        await File.WriteAllTextAsync(path, result.Value.GetRawText(), token);
        return path;
    }

    public ValueTask DisposeAsync()
    {
        var temporary = _temporary;
        return temporary is null ? ValueTask.CompletedTask : temporary.DisposeAsync();
    }

    private Task<SiteGitHubCliResult> RunAsync(string command, string[] arguments,
        CancellationToken token, int timeoutMilliseconds = SiteTokens.NodeTimeoutMilliseconds)
        => SiteGitHubEvidenceProcess.RunAsync(_repository,
            Path.Combine(_repository, SiteGitHubEvidenceTokens.CliModule), command, arguments,
            timeoutMilliseconds, token);

    private string[] InputAndMode(string mode, string? requestedRun)
    {
        var arguments = new List<string>
        {
            SiteGitHubEvidenceTokens.ArgumentInput + Capture,
            SiteGitHubEvidenceTokens.ArgumentMode + mode,
        };
        if (requestedRun is not null)
        {
            arguments.Add(SiteGitHubEvidenceTokens.ArgumentRequestedRun + requestedRun);
        }

        return [.. arguments];
    }

    private SiteTempDirectory RequiredTemporary => _temporary ??
        throw new InvalidOperationException(SiteGitHubEvidenceTokens.CaptureMissing);

    private void CreateTemporary() => _temporary = SiteTempDirectory.Create();
}

internal static class SiteGitHubEvidenceInputs
{
    internal static async Task CopyDirectoryAsync(string source, string destination, CancellationToken token)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in Directory.EnumerateDirectories(source, SiteGitHubEvidenceTokens.CaptureCopyPattern,
                     SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, SiteGitHubEvidenceTokens.CaptureCopyPattern,
                     SearchOption.AllDirectories))
        {
            token.ThrowIfCancellationRequested();
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllBytesAsync(target, await File.ReadAllBytesAsync(file, token), token);
        }
    }

    internal static string RequiredAbsoluteEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrWhiteSpace(value) || !Path.IsPathFullyQualified(value))
        {
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CaptureNotAbsolute);
        }

        return value;
    }

    internal static string RequiredRevisionEnvironment(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is null || !SiteTokens.MeasuredRevisionPattern.IsMatch(value))
        {
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CaptureMissing);
        }

        return value;
    }

}

internal sealed record SiteGitHubExpectedEvidence(System.Text.Json.JsonElement Run,
    System.Text.Json.JsonElement Job, System.Text.Json.JsonElement Artifact,
    string ArchiveSha256, long ArchiveBytes);

internal static class SiteGitHubExpectedEvidenceReader
{
    internal static async Task<SiteGitHubExpectedEvidence> ReadAsync(string capture, string archive,
        CancellationToken token)
    {
        var run = await ReadRootAsync(Path.Combine(capture, SiteGitHubEvidenceTokens.SelectedRunCapture), token);
        var jobs = await ReadRootAsync(Path.Combine(capture, SiteGitHubEvidenceTokens.JobsCapture), token);
        var artifacts = await ReadRootAsync(Path.Combine(capture, SiteGitHubEvidenceTokens.ArtifactsCapture), token);
        var runId = run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var attempt = run.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        var job = Flatten(jobs, SiteGitHubEvidenceTokens.Jobs)
            .Single(item => item.GetProperty(SiteTokens.Name).GetString() == SiteGitHubEvidenceTokens.ComparisonJobName &&
                item.GetProperty(SiteGitHubEvidenceTokens.RunIdField).GetInt64() == runId &&
                item.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32() == attempt);
        var artifact = Flatten(artifacts, SiteGitHubEvidenceTokens.Artifacts)
            .Single(item => item.GetProperty(SiteTokens.Name).GetString() == SiteGitHubEvidenceTokens.ComparisonSuiteArtifact &&
                item.GetProperty(SiteGitHubEvidenceTokens.WorkflowRun)
                    .GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64() == runId);
        var bytes = await File.ReadAllBytesAsync(archive, token);
        return new(run, job, artifact, Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes.LongLength);
    }

    internal static async Task<string[]> ExpectedMetadataPathsAsync(string capture, JsonElement selectedRun,
        CancellationToken token)
    {
        var pages = await ReadRootAsync(Path.Combine(capture, SiteGitHubEvidenceTokens.RunsCapture), token);
        var selectedId = selectedRun.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64();
        var summary = Flatten(pages, SiteGitHubEvidenceTokens.WorkflowRuns).Single(run =>
            run.GetProperty(SiteGitHubEvidenceTokens.ArtifactId).GetInt64() == selectedId);
        var currentAttempt = summary.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        var selectedAttempt = selectedRun.GetProperty(SiteGitHubEvidenceTokens.RunAttemptField).GetInt32();
        if (currentAttempt < selectedAttempt)
        {
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CaptureMissing);
        }

        var paths = new List<string>(SiteGitHubEvidenceTokens.RequiredCaptureFiles);
        for (var attempt = currentAttempt; attempt >= selectedAttempt; attempt--)
        {
            paths.Add(SiteGitHubRunSelectionMutations.AttemptRelativePath(selectedId, attempt,
                SiteGitHubEvidenceTokens.SelectedRunCapture));
            paths.Add(SiteGitHubRunSelectionMutations.AttemptRelativePath(selectedId, attempt,
                SiteGitHubEvidenceTokens.JobsCapture));
        }

        return [.. paths.OrderBy(path => path, StringComparer.Ordinal)];
    }

    private static async Task<System.Text.Json.JsonElement> ReadRootAsync(string path, CancellationToken token)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllBytesAsync(path, token));
        return document.RootElement.Clone();
    }

    private static System.Collections.Generic.IEnumerable<JsonElement> Flatten(JsonElement pages, string property)
    {
        return pages.EnumerateArray().SelectMany(page => page.GetProperty(property).EnumerateArray());
    }
}

internal static class SiteGitHubEvidenceProcess
{
    internal static async Task<SiteGitHubCliResult> RunAsync(string repository, string modulePath,
        string command, string[] arguments, int timeoutMilliseconds, CancellationToken token)
    {
        using var process = new Process { StartInfo = CreateStartInfo(repository, modulePath, command, arguments) };
        if (!process.Start())
        {
            throw new InvalidOperationException(SiteGitHubEvidenceTokens.CliFailure);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(timeoutMilliseconds);
        var stdout = SiteProcessOutput.ReadAsync(process.StandardOutput, SiteTokens.NodeOutputExceeded, timeout.Token);
        var stderr = SiteProcessOutput.ReadAsync(process.StandardError, SiteTokens.NodeOutputExceeded, timeout.Token);
        try
        {
            var exit = process.WaitForExitAsync(timeout.Token);
            var firstCompleted = await Task.WhenAny(exit, stdout, stderr);
            await firstCompleted;
            await exit;
            return ParseEnvelope(process.ExitCode, await stdout, await stderr);
        }
        catch (Exception)
        {
            try
            {
                await SiteProcessCleanup.StopAsync(process);
            }
            finally
            {
                await SiteProcessCleanup.ObserveCapturesAsync(process, stdout, stderr);
            }

            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string repository, string modulePath,
        string command, string[] arguments)
    {
        var start = new ProcessStartInfo(SiteTokens.NodeExecutable)
        {
            WorkingDirectory = repository,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.Environment.Clear();
        start.Environment[SiteGitHubEvidenceTokens.PathEnvironment] =
            Environment.GetEnvironmentVariable(SiteGitHubEvidenceTokens.PathEnvironment) ?? string.Empty;
        CopyNodeCoverageEnvironment(start);
        start.ArgumentList.Add(modulePath);
        start.ArgumentList.Add(command);
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    private static void CopyNodeCoverageEnvironment(ProcessStartInfo start)
    {
        var coverage = Environment.GetEnvironmentVariable(SiteGitHubEvidenceTokens.NodeV8CoverageEnvironment);
        if (!string.IsNullOrWhiteSpace(coverage))
        {
            start.Environment[SiteGitHubEvidenceTokens.NodeV8CoverageEnvironment] = coverage;
        }
    }

    private static SiteGitHubCliResult ParseEnvelope(int exitCode, string stdout, string stderr)
    {
        using var document = JsonDocument.Parse(stdout);
        var envelope = document.RootElement;
        var accepted = envelope.GetProperty(SiteGitHubEvidenceTokens.CliEnvelopeOk).GetBoolean();
        if (accepted)
        {
            return new(exitCode, true, envelope.GetProperty(SiteGitHubEvidenceTokens.CliEnvelopeResult).Clone(),
                null, null, stdout, stderr);
        }

        var error = envelope.GetProperty(SiteGitHubEvidenceTokens.CliEnvelopeError);
        return new(exitCode, false, error.Clone(), error.GetProperty(SiteGitHubEvidenceTokens.ErrorCode).GetString(),
            error.GetProperty(SiteGitHubEvidenceTokens.ErrorMessage).GetString(), stdout, stderr);
    }
}
