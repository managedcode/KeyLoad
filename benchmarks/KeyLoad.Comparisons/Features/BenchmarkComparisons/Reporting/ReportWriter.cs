using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.Comparisons;

/// <summary>Writes the comparison report files and formats the existing Markdown summary.</summary>
public static class ReportWriter
{
    private const string JsonFileName = "results.json";
    private const string MarkdownFileName = "results.md";
    private const string CsvFileName = "samples.csv";
    private const int JsonFileBufferBytes = 65_536;

    /// <summary>Gets the published JSON serializer settings for raw comparison results.</summary>
    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();
    /// <summary>Writes JSON, Markdown, and CSV reports to the specified directory.</summary>
    public static async Task WriteAsync(ComparisonReport report, string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(report);
        report.ValidateConfiguration();
        ArgumentNullException.ThrowIfNull(directory);
        Directory.CreateDirectory(directory);
        cancellationToken.ThrowIfCancellationRequested();
        var jsonPath = Path.Combine(directory, JsonFileName);
        await using (var stream = new FileStream(jsonPath, new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = JsonFileBufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        }))
        {
            await JsonSerializer.SerializeAsync(stream, StreamedComparisonReport.Create(report), JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        await File.WriteAllTextAsync(Path.Combine(directory, MarkdownFileName), Markdown(report), cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        await ReportCsvWriter.WriteAsync(report, Path.Combine(directory, CsvFileName), Number, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Formats the established case summary for the console and Markdown file.</summary>
    public static string Markdown(ComparisonReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        report.ValidateConfiguration();
        var text = new StringBuilder("# KeyLoad comparison results\n\n");
        if (report.Options is { } options)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"Run: {report.RunId}. Seed: {options.Seed}. Corpus SHA256: `{report.DatasetSha256}`.");
            text.AppendLine(CultureInfo.CurrentCulture, $"\n{options.Documents} documents, {options.PayloadBytes} UTF-8 bytes/document, {options.Dimensions} float32 dimensions, top-{options.TopK}, {options.Operations} attempts/case, concurrency {options.Concurrency}, {options.Warmup} warmup requests, {options.Repetitions} repetitions.");
            text.AppendLine(CultureInfo.CurrentCulture, $"Graph: {Math.Min(options.Documents, options.GraphVertices)} vertices in two disconnected cyclic components; fan-out up to {options.GraphFanOut}; traversal depth {options.GraphDepth}. Neighbors/traversal return sorted distinct reachable IDs, excluding the start vertex.");
        }
        else if (report.ScaledProfile is { } scaled)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"Run: {report.RunId}. Profile: {scaled.Id}. Seed: {scaled.Seed}. Corpus SHA256: `{report.DatasetSha256}`.");
            text.AppendLine(CultureInfo.CurrentCulture, $"\n{scaled.Documents} native documents, {scaled.PayloadBytes} UTF-8 bytes/document, {scaled.Operations} measured operations/case, concurrency {scaled.Concurrency}; closed-loop S1.");
        }
        else
        {
            throw new InvalidOperationException("A comparison report must carry exactly one configuration.");
        }
        text.AppendLine(CultureInfo.CurrentCulture, $"\nHost: {report.HostOs}; {report.Architecture}; {report.LogicalProcessors} logical processors; {report.Runtime}. Load: {report.LoadModel}. Storage: {report.Storage}. Source revision: {report.SourceRevision ?? "unrecorded"}.");
        text.AppendLine("\nExploratory development run. Engine topology, resource allocation, authorization, transports and write guarantees differ. These rows show observed speed, not an equal-durability or production winner. External engines run in containers; Docker Desktop adds VM overhead where used. KeyLoad voters run as host processes. Same-host voters do not represent independent failure domains. No primary performance target has been qualified.\n");
        text.AppendLine("| Engine | Version | Topology | Write acknowledgement | Read / transport / policy | Image |\n|---|---|---|---|---|---|");
        foreach (var target in report.Targets)
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"| {target.Name} | {target.Version} | {target.Topology} | {target.WriteAcknowledgement} | {target.ReadContract}; {target.Transport}; {target.Authorization} | {target.Image ?? "source checkout"} |");
        }
        text.AppendLine("\nCases are ordered by verified useful operations/s within each scenario and repetition. Latency includes failed attempts. Setup, warmup and correctness checks are excluded from timings; writes are read back and vectors must match the exhaustive oracle, including JSON projection. QueueCycle counts unique completions and includes enqueue, receive and ACK.\n");
        var latencyColumns = report.ScaledProfile is null ? "p50 ms | p95 ms | p99 ms" : "p50 sample estimate ms | p95 sample estimate ms | p99 sample estimate ms";
        text.AppendLine(CultureInfo.CurrentCulture,
            $"| Scenario | Run | Engine | Status | Successful / attempts | Useful ops/s | {latencyColumns} | Unique completed |\n|---|---|---|---|---:|---:|---:|---:|---:|---:|");
        foreach (var item in report.Cases.OrderBy(item => item.Scenario).ThenBy(item => item.Repetition).ThenByDescending(item => item.Measurement?.UsefulOperationsPerSecond ?? -1))
        {
            var m = item.Measurement;
            text.AppendLine(CultureInfo.CurrentCulture, $"| {item.Scenario} | {item.Repetition + 1} | {item.Target} | {item.Status} | {(m is null ? "—" : $"{m.Successes} / {m.Attempts}")} | {Number(m?.UsefulOperationsPerSecond)} | {Number(m?.Latency.P50Ms)} | {Number(m?.Latency.P95Ms)} | {Number(m?.Latency.P99Ms)} | {m?.UniqueCompletedMessages.ToString(CultureInfo.InvariantCulture) ?? "—"} |");
        }
        foreach (var item in report.Cases.Where(item => item.Status == "failed" && item.Detail is not null))
        {
            text.AppendLine(CultureInfo.CurrentCulture, $"\n{item.Target} / {item.Scenario} / repetition {item.Repetition + 1}: {item.Detail}");
        }
        text.AppendLine("\nQueue enqueue/receive/ACK percentiles and raw attempt samples are in results.json and samples.csv. Unsupported operations have null measurements. Repetitions remain separate; no best-run-only aggregate is selected.\n");
        text.AppendLine("Client resource measurements cover the load generator process, drivers and sampler during requests, excluding setup/warmup/validation. CPU and allocation deltas are per case; RSS is observed every 50 ms and at the boundaries. These values do not measure database CPU/RAM and are not additive per-engine resource budgets.\n");
        AppendClientResourceTable(text, report);
        text.AppendLine("Remaining qualification: matched synchronous RF3 baselines and hardware budgets, common application transport/policy, open-loop offered load, larger-than-RAM data, ANN/filtered recall, Marten/Wolverine combined workflows, endurance and fault injection. Smoke runs check adapter correctness; their short timings are not performance qualification.");
        return text.ToString();
    }

    private static void AppendClientResourceTable(StringBuilder text, ComparisonReport report)
    {
        text.AppendLine("| Scenario | Run | Engine | Generator CPU seconds | Generator allocated MiB | Generator observed peak RSS MiB |\n|---|---|---|---:|---:|---:|");
        foreach (var item in report.Cases.Where(item => item.Measurement?.ClientResources is not null))
        {
            var resource = item.Measurement!.ClientResources!;
            text.AppendLine(CultureInfo.CurrentCulture, $"| {item.Scenario} | {item.Repetition + 1} | {item.Target} | {Number(resource.CpuSeconds)} | {Number(resource.AllocatedBytes / 1048576d)} | {Number(resource.PeakObservedWorkingSetBytes / 1048576d)} |");
        }
    }
    private static string Number(double? number) => number?.ToString("F3", CultureInfo.InvariantCulture) ?? "";

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };
        ComparisonContractJson.Configure(options);
        return options;
    }
}
