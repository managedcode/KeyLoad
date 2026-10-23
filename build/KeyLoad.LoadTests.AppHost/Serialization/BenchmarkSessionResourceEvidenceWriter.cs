using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.LoadTests.AppHost.Features.BenchmarkComparisons;

/// <summary>Retains original lifetime-wide execution container observations without inventing per-case attribution.</summary>
internal static class BenchmarkSessionResourceEvidenceWriter
{
    private const string StateFile = "session-state.json";
    private const string Sidecar = "session-resource-evidence.json";
    private const string Schema = "session-resource-evidence";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static async Task WriteAsync(string sessionId, BenchmarkSessionSelection selection, string output,
        ScaleServerObservationSnapshot observations, IOptions<ScaleServerResourceOptions> resourceOptions,
        IOptions<BenchmarkProvenanceOptions> provenanceOptions, CancellationToken token)
    {
        var settings = resourceOptions.Value;
        var provenance = provenanceOptions.Value;
        var state = new FileInfo(Path.Combine(output, StateFile));
        if (!state.Exists)
        {
            return;
        }
        if (state.LinkTarget is not null || state.Length <= 0 || state.Length > settings.MaxWorkerBytes)
        {
            throw new InvalidDataException(Schema);
        }
        var bytes = await File.ReadAllBytesAsync(state.FullName, token);
        var document = new BenchmarkSessionResourceEvidence(Schema, sessionId,
            provenance.SourceRevision, provenance.WorkflowRunId,
            provenance.RunAttempt, provenance.JobId,
            selection.Target, selection.NodeCount, Convert.ToHexStringLower(SHA256.HashData(bytes)), observations);
        bytes = JsonSerializer.SerializeToUtf8Bytes(document, Json);
        if (bytes.Length > settings.MaxSidecarBytes)
        {
            throw new InvalidDataException(Schema);
        }
        await using var file = new FileStream(Path.Combine(output, Sidecar), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, settings.RuntimeReadBufferBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        await file.WriteAsync(bytes, token);
        await file.FlushAsync(token);
    }

    private sealed record BenchmarkSessionResourceEvidence(string SchemaVersion, string SessionId,
        string? SourceRevision, string? RunId, string? Attempt, string? JobId, string Target, int NodeCount,
        string SessionStateSha256, ScaleServerObservationSnapshot Observations);
}
