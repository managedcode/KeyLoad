using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using KeyLoad.AppHost.Features.BenchmarkComparisons;
using SessionFields = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.BenchmarkSessionEvidenceKeys;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Joins native runtime identities, immutable case bytes and the completed original TUnit report.</summary>
internal static class BenchmarkSessionManifest
{
    private const string State = "session-state.json";
    private const string Manifest = "session.json";
    private const string NativeResults = "NativeTestResults";
    private const string RuntimeImport = "runtime-import.json";
    private const string Resources = "session-resource-evidence.json";
    private const string ImagesField = "images";
    private const string ServerImageField = "server";
    private const long MaximumBytes = 16 * 1024 * 1024;
    private const long MaximumReportBytes = 256 * 1024 * 1024;
    private const string Invalid = "BenchmarkSessionOriginalEvidenceInvalid";
    private static readonly string[] CaseIds =
        ["load-million", "index", "sequential-read", "random-read", "search", "mixed", "verify-final"];
    private static readonly string[] NativeNames =
        ["LoadMillion", "BuildIndexes", "SequentialRead", "RandomRead", "Search", "Mixed", "VerifyFinalState"];

    internal static async Task WriteAsync(string root, BenchmarkSessionCallerOptions selected, int exit, bool qualificationFailed, CancellationToken token)
    {
        var bytes = await ReadAsync(Path.Combine(root, State), token);
        var state = JsonNode.Parse(bytes)?.AsObject() ?? throw new InvalidDataException(Invalid);
        ValidateCohort(state, selected);
        await VerifyCasesAsync(root, state[SessionFields.Cases]!.AsArray(), token);
        var runtimeImport = await ReadAsync(Path.Combine(root, RuntimeImport), token);
        state[SessionFields.OuterRuntime] = new JsonObject { [SessionFields.Path] = RuntimeImport, [SessionFields.Sha256] = Hash(runtimeImport) };
        state[SessionFields.NativeReport] = await NativeReportAsync(root, exit, token);
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
            state[SessionFields.Outcome] = "failure";
            state[SessionFields.Reason] = "NativeSessionQualificationFailed";
        }
        if (exit != 0)
        {
            state[SessionFields.Outcome] = "failure";
            state[SessionFields.Reason] = "NativeSessionTestFailure";
        }
        var output = JsonSerializer.SerializeToUtf8Bytes(state);
        await using var file = new FileStream(Path.Combine(root, Manifest), FileMode.CreateNew, FileAccess.Write, FileShare.None,
            bufferSize: 4096, useAsync: true);
        await file.WriteAsync(output, token);
        await file.FlushAsync(token);
    }

    private static void ValidateCohort(JsonObject state, BenchmarkSessionCallerOptions selected)
    {
        var provenance = BenchmarkProvenanceRegistration.Bind().Value;
        Require(state[SessionFields.SchemaVersion]?.GetValue<string>() == "session-v1"
            && state[SessionFields.SessionId]?.GetValue<string>() == selected.SessionId
            && state[SessionFields.Target]?.GetValue<string>() == selected.Target
            && state[SessionFields.NodeCount]?.GetValue<int>() == selected.NodeCount
            && state[SessionFields.SourceRevision]?.GetValue<string>() == provenance.SourceRevision
            && state[SessionFields.RunId]?.GetValue<long>().ToString(CultureInfo.InvariantCulture) == provenance.WorkflowRunId
            && state[SessionFields.Attempt]?.GetValue<int>().ToString(CultureInfo.InvariantCulture) == provenance.RunAttempt
            && state[SessionFields.JobId]?.GetValue<long>().ToString(CultureInfo.InvariantCulture) == provenance.JobId
            && state[SessionFields.Repository]?.GetValue<string>() == provenance.Repository
            && state[SessionFields.Reference]?.GetValue<string>() == provenance.Reference
            && state[SessionFields.Workflow]?.GetValue<string>() == provenance.Workflow);
    }

    private static async Task<JsonObject> NativeReportAsync(string root, int exit, CancellationToken token)
    {
        var native = Directory.EnumerateFiles(Path.Combine(root, NativeResults), "*.trx", SearchOption.AllDirectories)
            .Take(2).ToArray();
        Require(native.Length == 1);
        var reportBytes = await ReadAsync(native[0], token);
        using var source = new MemoryStream(reportBytes, writable: false);
        var report = XDocument.Load(source);
        var outcomes = report.Descendants().Where(element => element.Name.LocalName == SessionFields.NativeResultElement).ToArray();
        Require(outcomes.Length == NativeNames.Length && NativeNames.All(name =>
            outcomes.Count(result => result.Attribute(SessionFields.NativeTestName)?.Value == name) == 1));
        var success = outcomes.All(result => result.Attribute(SessionFields.Outcome)?.Value == "Passed");
        Require(exit != 0 || success);
        return new JsonObject
        {
            [SessionFields.Path] = Path.GetRelativePath(root, native[0]).Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
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
            Require(path == "reports/" + id + ".json");
            var file = new FileInfo(Path.Combine(root, path));
            Require(file.Exists && file.LinkTarget is null && file.Length > 0 && file.Length <= MaximumReportBytes);
            await using var input = File.OpenRead(file.FullName);
            Require(Convert.ToHexStringLower(await SHA256.HashDataAsync(input, token))
                == selected[SessionFields.Sha256]!.GetValue<string>());
        }
    }

    private static async Task<byte[]> ReadAsync(string path, CancellationToken token, long maximumBytes = MaximumBytes)
    {
        var file = new FileInfo(path);
        Require(file.Exists && file.LinkTarget is null && file.Length > 0 && file.Length <= maximumBytes);
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
