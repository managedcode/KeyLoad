using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

public static class ReportWriter
{
    public static JsonSerializerOptions JsonOptions { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    };
    public static async Task WriteAsync(ComparisonReport report, string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(report, JsonOptions), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(directory, "results.md"), Markdown(report), cancellationToken);
        // CSV keeps every attempt, including errors/timeouts and separate queue stages.
        var csv = new StringBuilder("target,scenario,repetition,operation,worker,started_ms,completed_ms,latency_ms,success,error,payload_bytes,completed_message_id,enqueue_ms,receive_ms,ack_ms\n");
        foreach (var item in report.Cases)
            foreach (var sample in item.Samples)
                csv.AppendLine(string.Join(",", Quote(item.Target), item.Scenario, item.Repetition, sample.Operation, sample.Worker,
                    Number(sample.StartedMs), Number(sample.CompletedMs), Number(sample.LatencyMs), sample.Success, Quote(sample.Error ?? ""), sample.PayloadBytes,
                    Quote(sample.CompletedMessageId ?? ""), Number(sample.Queue?.EnqueueMs), Number(sample.Queue?.ReceiveMs), Number(sample.Queue?.AckMs)));
        await File.WriteAllTextAsync(Path.Combine(directory, "samples.csv"), csv.ToString(), cancellationToken);
    }

    public static string Markdown(ComparisonReport report)
    {
        var text = new StringBuilder("# KeyLoad comparison results\n\n");
        text.AppendLine($"Run: {report.RunId}. Seed: {report.Options.Seed}. Corpus SHA256: `{report.DatasetSha256}`.");
        text.AppendLine($"\n{report.Options.Documents} documents, {report.Options.PayloadBytes} UTF-8 bytes/document, {report.Options.Dimensions} float32 dimensions, top-{report.Options.TopK}, {report.Options.Operations} attempts/case, concurrency {report.Options.Concurrency}, {report.Options.Warmup} warmup requests, {report.Options.Repetitions} repetitions.");
        text.AppendLine($"Graph: {Math.Min(report.Options.Documents, report.Options.GraphVertices)} vertices in two disconnected cyclic components; fan-out up to {report.Options.GraphFanOut}; traversal depth {report.Options.GraphDepth}. Neighbors/traversal return sorted distinct reachable IDs, excluding the start vertex.");
        text.AppendLine($"\nHost: {report.HostOs}; {report.Architecture}; {report.LogicalProcessors} logical processors; {report.Runtime}. Load: {report.LoadModel}. Storage: {report.Storage}. Source revision: {report.SourceRevision ?? "unrecorded"}.");
        text.AppendLine("\nExploratory development run. Engine topology, resource allocation, authorization, transports and write guarantees differ. These rows show observed speed, not an equal-durability or production winner. External engines run in containers; Docker Desktop adds VM overhead where used. KeyLoad voters run as host processes. Same-host voters do not represent independent failure domains. No primary performance target has been qualified.\n");
        text.AppendLine("| Engine | Version | Topology | Write acknowledgement | Read / transport / policy | Image |\n|---|---|---|---|---|---|");
        foreach (var target in report.Targets)
            text.AppendLine($"| {target.Name} | {target.Version} | {target.Topology} | {target.WriteAcknowledgement} | {target.ReadContract}; {target.Transport}; {target.Authorization} | {target.Image ?? "source checkout"} |");
        text.AppendLine("\nCases are ordered by verified useful operations/s within each scenario and repetition. Latency includes failed attempts. Setup, warmup and correctness checks are excluded from timings; writes are read back and vectors must match the exhaustive oracle, including JSON projection. QueueCycle counts unique completions and includes enqueue, receive and ACK.\n");
        text.AppendLine("| Scenario | Run | Engine | Status | Successful / attempts | Useful ops/s | p50 ms | p95 ms | p99 ms | Unique completed |\n|---|---|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var item in report.Cases.OrderBy(item => item.Scenario).ThenBy(item => item.Repetition).ThenByDescending(item => item.Measurement?.UsefulOperationsPerSecond ?? -1))
        {
            var m = item.Measurement;
            text.AppendLine($"| {item.Scenario} | {item.Repetition + 1} | {item.Target} | {item.Status} | {(m is null ? "—" : $"{m.Successes} / {m.Attempts}")} | {Number(m?.UsefulOperationsPerSecond)} | {Number(m?.Latency.P50Ms)} | {Number(m?.Latency.P95Ms)} | {Number(m?.Latency.P99Ms)} | {m?.UniqueCompletedMessages.ToString(CultureInfo.InvariantCulture) ?? "—"} |");
        }
        foreach (var item in report.Cases.Where(item => item.Status == "failed" && item.Detail is not null))
            text.AppendLine($"\n{item.Target} / {item.Scenario} / repetition {item.Repetition + 1}: {item.Detail}");
        text.AppendLine("\nQueue enqueue/receive/ACK percentiles and raw attempt samples are in results.json and samples.csv. Unsupported operations have null measurements. Repetitions remain separate; no best-run-only aggregate is selected.\n");
        text.AppendLine("Client resource measurements cover the load generator process, drivers and sampler during requests, excluding setup/warmup/validation. CPU and allocation deltas are per case; RSS is observed every 50 ms and at the boundaries. These values do not measure database CPU/RAM and are not additive per-engine resource budgets.\n");
        text.AppendLine("| Scenario | Run | Engine | Generator CPU seconds | Generator allocated MiB | Generator observed peak RSS MiB |\n|---|---|---|---:|---:|---:|");
        foreach (var item in report.Cases.Where(item => item.Measurement?.ClientResources is not null))
        {
            var resource = item.Measurement!.ClientResources!;
            text.AppendLine($"| {item.Scenario} | {item.Repetition + 1} | {item.Target} | {Number(resource.CpuSeconds)} | {Number(resource.AllocatedBytes / 1048576d)} | {Number(resource.PeakObservedWorkingSetBytes / 1048576d)} |");
        }
        text.AppendLine("Remaining qualification: matched synchronous RF3 baselines and hardware budgets, common application transport/policy, open-loop offered load, larger-than-RAM data, ANN/filtered recall, Marten/Wolverine combined workflows, endurance and fault injection. Smoke runs check adapter correctness; their short timings are not performance qualification.");
        return text.ToString();
    }
    private static string Number(double? number) => number?.ToString("F3", CultureInfo.InvariantCulture) ?? "";
    private static string Quote(string value) => "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
