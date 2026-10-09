using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal static class ScaleServerResourceEvidenceWriter
{
    private const string WorkerFileName = "worker.json";
    private const string DocumentWorkerFileName = "document-worker.json";
    private const string SidecarFileName = "server-resource-evidence.json";
    private const string Schema = "server-resource-evidence.v2";
    private const string ExceededSidecar = "Server resource evidence exceeded its bound.";
    private const string ExceededWorker = "The isolated worker report exceeded its bound.";
    private const int EmptyValue = 0;
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    internal static async Task WriteAsync(ComparisonWorkerSelection selection, string output,
        ScaleServerObservationSnapshot observations, IOptions<ScaleServerResourceOptions> resourceOptions,
        IOptions<BenchmarkProvenanceOptions> provenanceOptions, CancellationToken token)
    {
        var settings = resourceOptions.Value;
        var provenance = provenanceOptions.Value;
        var workerPath = Path.Combine(output, selection.DocumentWorkload is null ? WorkerFileName : DocumentWorkerFileName);
        if (!File.Exists(workerPath) || new FileInfo(workerPath).Length > settings.MaxWorkerBytes)
        {
            return;
        }

        var workerHash = await HashAsync(workerPath, settings, token);
        var document = new ScaleServerResourceEvidence(Schema, provenance.SourceRevision ?? string.Empty,
            provenance.WorkflowRunId ?? string.Empty, provenance.RunAttempt ?? string.Empty,
            provenance.JobId ?? string.Empty, selection.Target, selection.NodeCount,
            selection.Scenario.ToString(), selection.Profile, workerHash, observations.Hardware,
            observations.AppHostEnvelope, observations.Containers, observations.MissingEvidence,
            observations.Qualified, observations.ObservationPolicy);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, SerializerOptions);
        if (bytes.Length > settings.MaxSidecarBytes)
        {
            throw new InvalidDataException(ExceededSidecar);
        }

        Directory.CreateDirectory(output);
        await using var stream = new FileStream(Path.Combine(output, SidecarFileName), FileMode.CreateNew,
            FileAccess.Write, FileShare.None, settings.NativeReadBufferBytes,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }

    private static async Task<string> HashAsync(string path, ScaleServerResourceOptions settings, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            settings.NativeReadBufferBytes, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[settings.NativeReadBufferBytes];
        long total = EmptyValue;
        while (true)
        {
            var count = await stream.ReadAsync(buffer, token);
            if (count == EmptyValue)
            { break; }
            if (total > settings.MaxWorkerBytes - count)
            { throw new InvalidDataException(ExceededWorker); }
            total += count;
            hash.AppendData(buffer, EmptyValue, count);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
