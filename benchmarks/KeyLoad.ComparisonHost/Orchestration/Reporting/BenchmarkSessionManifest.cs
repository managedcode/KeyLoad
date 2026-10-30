using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using KeyLoad.Comparisons;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;
using SessionFields = KeyLoad.ComparisonHost.BenchmarkOrchestration.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

/// <summary>Joins execution runtime identities, immutable case bytes and the completed original TUnit report.</summary>
internal static class BenchmarkSessionManifest
{
    private const string LoadMillionValue = "load-million";
    private const string IndexValue = "index";
    private const string SequentialReadValue = "sequential-read";
    private const string RandomReadValue = "random-read";
    private const string SearchValue = "search";
    private const string MixedValue = "mixed";
    private const string VerifyFinalValue = "verify-final";
    private const string ExecutionLoadMillionValue = "LoadMillion";
    private const string BuildIndexesValue = "BuildIndexes";
    private const string ExecutionSequentialReadValue = "SequentialRead";
    private const string ExecutionRandomReadValue = "RandomRead";
    private const string ExecutionSearchValue = "Search";
    private const string ExecutionMixedValue = "Mixed";
    private const string VerifyFinalStateValue = "VerifyFinalState";
    private const string PrepareModelsValue = "PrepareModels", VectorExactValue = "VectorExact", QueueCycleValue = "QueueCycle";
    private const string StreamAppendValue = "StreamAppend", StreamReadValue = "StreamRead", StreamMixedValue = "StreamMixed";
    private const string FailureOutcome = "failure";
    private const string QualificationFailedReason = "SessionQualificationFailed";
    private const int EmptyCount = 0;
    private const string ClientFailedReason = "SessionTestFailure";
    private const string SessionValue = "session";
    private const string TrxValue = "*.trx";
    private const int DuplicateDetectionLimit = 2;
    private const int SingleItemCount = 1;
    private const string PassedValue = "Passed";
    private const string ReportsValue = "reports/";
    private const string JsonFileName = ".json";

    private const string State = "session-state.json";
    private const string Manifest = "session.json";
    private const string RuntimeResults = "RuntimeTestResults";
    private const string RuntimeImport = "runtime-import.json";
    private const string Resources = "session-resource-evidence.json";
    private const string ImagesField = "images";
    private const string ServerImageField = "server";
    private const long MaximumBytes = 16 * 1024 * 1024;
    private const long MaximumReportBytes = 256 * 1024 * 1024;
    private const string Invalid = "BenchmarkSessionOriginalEvidenceInvalid";
    private static readonly string[] CaseIds =
        [LoadMillionValue, IndexValue, SequentialReadValue, RandomReadValue, SearchValue, MixedValue, VerifyFinalValue,
            BenchmarkSessionModelCases.Prepare, BenchmarkSessionModelCases.Vector, BenchmarkSessionModelCases.Queue, BenchmarkSessionModelCases.Append,
            BenchmarkSessionModelCases.Read, BenchmarkSessionModelCases.Mixed];
    private static readonly string[] RuntimeNames =
        [ExecutionLoadMillionValue, BuildIndexesValue, ExecutionSequentialReadValue, ExecutionRandomReadValue, ExecutionSearchValue, ExecutionMixedValue, VerifyFinalStateValue,
            PrepareModelsValue, VectorExactValue, QueueCycleValue, StreamAppendValue, StreamReadValue, StreamMixedValue];

    internal static async Task WriteAsync(string root, BenchmarkSessionCallerOptions selected, int exit, bool qualificationFailed, CancellationToken token)
    {
        var bytes = await ReadAsync(Path.Combine(root, State), token);
        var state = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException(Invalid);
        ValidateCohort(state, selected);
        await VerifyCasesAsync(root, state[SessionFields.Cases]!.AsArray(), token);
        state[SessionFields.OuterRuntime] = null;
        if (state[SessionFields.RunId] is not null)
        {
            var runtimeImport = await ReadAsync(Path.Combine(root, RuntimeImport), token);
            state[SessionFields.OuterRuntime] = new JsonObject { [SessionFields.Path] = RuntimeImport, [SessionFields.Sha256] = Hash(runtimeImport) };
        }
        state[SessionFields.RuntimeReport] = await RuntimeReportAsync(root, exit, token);
        var qualification = await ReadAsync(Path.Combine(root, BenchmarkSessionQualifications.FileName), token);
        state[SessionFields.QualificationReport] = new JsonObject
        {
            [SessionFields.Path] = BenchmarkSessionQualifications.FileName,
            [SessionFields.Sha256] = Hash(qualification)
        };
        if (state[ImagesField]!.AsObject()[ServerImageField] is null)
        {
            state[SessionFields.ResourceReport] = null;
        }
        else
        {
            var resources = await ReadAsync(Path.Combine(root, Resources), token);
            state[SessionFields.ResourceReport] = new JsonObject { [SessionFields.Path] = Resources, [SessionFields.Sha256] = Hash(resources) };
        }
        if (qualificationFailed)
        {
            state[SessionFields.Outcome] = FailureOutcome;
            state[SessionFields.Reason] = QualificationFailedReason;
        }
        if (exit != EmptyCount)
        {
            state[SessionFields.Outcome] = FailureOutcome;
            state[SessionFields.Reason] = ClientFailedReason;
        }
        var output = JsonSerializer.SerializeToUtf8Bytes(state);
        await using var file = new FileStream(Path.Combine(root, Manifest), FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: ExecutionPolicyFixture.Harness().Value.TeardownReceiptFileBufferBytes, useAsync: true);
        await file.WriteAsync(output, token);
        await file.FlushAsync(token);
    }

    internal static async Task ValidateOutputsAsync(string root, BenchmarkSessionCallerOptions selected, int exit, CancellationToken token)
    {
        var state = JsonNode.Parse(await ReadAsync(Path.Combine(root, State), token))?.AsObject()
            ?? throw new InvalidDataException(Invalid);
        ValidateCohort(state, selected);
        await VerifyCasesAsync(root, state[SessionFields.Cases]!.AsArray(), token);
        _ = await RuntimeReportAsync(root, exit, token);
    }

    private static void ValidateCohort(JsonObject state, BenchmarkSessionCallerOptions selected)
    {
        var provenance = BenchmarkProvenanceRegistration.Bind().Value;
        Require(state[SessionFields.SchemaVersion]?.GetValue<string>() == SessionValue
            && state[SessionFields.SessionId]?.GetValue<string>() == selected.SessionId
            && state[SessionFields.Target]?.GetValue<string>() == selected.Target
            && state[SessionFields.NodeCount]?.GetValue<int>() == selected.NodeCount);
        if (!string.Equals(provenance.Actions, bool.TrueString, StringComparison.OrdinalIgnoreCase))
        {
            Require(new[] { SessionFields.SourceRevision, SessionFields.RunId, SessionFields.Attempt, SessionFields.JobId,
                SessionFields.Repository, SessionFields.Reference, SessionFields.Workflow }.All(field => state[field] is null));
            return;
        }
        Require(state[SessionFields.SourceRevision]?.GetValue<string>() == provenance.SourceRevision
            && state[SessionFields.RunId]?.GetValue<long>().ToString(CultureInfo.InvariantCulture) == provenance.WorkflowRunId
            && state[SessionFields.Attempt]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) == provenance.RunAttempt
            && state[SessionFields.JobId]?.GetValue<long>().ToString(CultureInfo.InvariantCulture) == provenance.JobId
            && state[SessionFields.Repository]?.GetValue<string>() == provenance.Repository
            && state[SessionFields.Reference]?.GetValue<string>() == provenance.Reference
            && state[SessionFields.Workflow]?.GetValue<string>() == provenance.Workflow);
    }

    private static async Task<JsonObject> RuntimeReportAsync(string root, int exit, CancellationToken token)
    {
        var execution = Directory.EnumerateFiles(Path.Combine(root, RuntimeResults), TrxValue, SearchOption.AllDirectories)
            .Take(DuplicateDetectionLimit).ToArray();
        Require(execution.Length == SingleItemCount);
        var reportBytes = await ReadAsync(execution[EmptyCount], token);
        using var source = new MemoryStream(reportBytes, writable: false);
        var report = XDocument.Load(source);
        var outcomes = report.Descendants().Where(element => element.Name.LocalName == SessionFields.ResultElement).ToArray();
        Require(outcomes.Length == RuntimeNames.Length && RuntimeNames.All(name =>
            outcomes.Count(result => result.Attribute(SessionFields.RuntimeTestName)?.Value == name) == SingleItemCount));
        var success = outcomes.All(result => result.Attribute(SessionFields.Outcome)?.Value == PassedValue);
        Require(exit != EmptyCount || success);
        return new JsonObject
        {
            [SessionFields.Path] = Path.GetRelativePath(root, execution[EmptyCount]).Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            [SessionFields.Sha256] = Hash(reportBytes)
        };
    }

    private static async Task VerifyCasesAsync(string root, JsonArray cases, CancellationToken token)
    {
        Require(cases.Count == CaseIds.Length);
        foreach (var id in CaseIds)
        {
            var selected = cases.Single(item => item![SessionFields.Id]!.GetValue<string>() == id)!;
            var path = selected[SessionFields.Path]!.GetValue<string>();
            Require(path == ReportsValue + id + JsonFileName);
            var file = new FileInfo(Path.Combine(root, path));
            Require(file.Exists && file.LinkTarget is null && file.Length > EmptyCount && file.Length <= MaximumReportBytes);
            await using var input = File.OpenRead(file.FullName);
            Require(Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token))
                == selected[SessionFields.Sha256]!.GetValue<string>());
        }
    }

    private static async Task<byte[]> ReadAsync(string path, CancellationToken token, long maximumBytes = MaximumBytes)
    {
        var file = new FileInfo(path);
        Require(file.Exists && file.LinkTarget is null && file.Length > EmptyCount && file.Length <= maximumBytes);
        return await File.ReadAllBytesAsync(path, token);
    }

    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void Require(bool accepted)
    {
        if (!accepted)
        {
            throw new InvalidDataException(Invalid);
        }
    }
}
