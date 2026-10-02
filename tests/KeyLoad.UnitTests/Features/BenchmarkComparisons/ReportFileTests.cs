using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>REQ-BC-010 / AC-MP-010: report files retain the published raw-attempt contract.</summary>
internal sealed class ReportFileTests
{
    [Test]
    public async Task JsonAndCsvPreserveSchemaEveryAttemptAndCsvEscaping()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-report-" + Guid.NewGuid().ToString("N"));
        var report = Report();
        try
        {
            await ReportWriter.WriteAsync(report, directory, TestContext.Current!.Execution.CancellationToken);

            var json = await File.ReadAllTextAsync(Path.Combine(directory, "results.json"));
            await Assert.That(json).IsEqualTo(JsonSerializer.Serialize(report, ReportWriter.JsonOptions));
            await Assert.That((await File.ReadAllBytesAsync(Path.Combine(directory, "results.json")))
                .SequenceEqual(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(report, ReportWriter.JsonOptions)))).IsTrue();
            var restored = JsonSerializer.Deserialize<ComparisonReport>(json, ReportWriter.JsonOptions)!;
            await Assert.That(restored.SchemaVersion).IsEqualTo(report.SchemaVersion);
            await Assert.That(restored.Cases.Length).IsEqualTo(2);
            await Assert.That(restored.Cases.Sum(item => item.Samples.Length)).IsEqualTo(3);
            await Assert.That(restored.Cases[0].Samples[0].Success).IsTrue();
            await Assert.That(restored.Cases[0].Samples[1].Success).IsFalse();
            await Assert.That(restored.Cases[0].Samples[1].Error).IsEqualTo("timeout\n\"Δ\"");
            await Assert.That(restored.Cases[0].Samples[1].Queue).IsEqualTo(new QueueTimings(1.25, 2.5, 3.75));
            await Assert.That(restored.Cases[1].Samples[0].Success).IsFalse();

            var csv = await File.ReadAllTextAsync(Path.Combine(directory, "samples.csv"));
            var expected = "target,scenario,repetition,operation,worker,started_ms,completed_ms,latency_ms,success,error,payload_bytes,completed_message_id,enqueue_ms,receive_ms,ack_ms\n"
                + "\"node,\"\"β\"\"\",QueueCycle,1,0,2,1.000,2.000,1.000,True,\"\",12,\"id-α\",1.000,2.000,3.000" + Environment.NewLine
                + "\"node,\"\"β\"\"\",QueueCycle,1,1,2,2.000,4.500,2.500,False,\"timeout\n\"\"Δ\"\"\",7,\"msg,\"\"β\"\"\",1.250,2.500,3.750" + Environment.NewLine
                + "\"other\",PointRead,0,2,0,4.000,5.000,1.000,False,\"unavailable\",0,\"\",,," + Environment.NewLine;
            await Assert.That(csv).IsEqualTo(expected);
            await Assert.That(await File.ReadAllTextAsync(Path.Combine(directory, "results.md"))).IsEqualTo(ReportWriter.Markdown(report));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Test]
    public async Task CancelledReportDoesNotCreateOutputDirectoryOrFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-report-cancel-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ReportWriter.WriteAsync(Report(), directory, cancellation.Token));
        await Assert.That(Directory.Exists(directory)).IsFalse();
    }

    [Test]
    public async Task CancellationWhileJsonIsGrowingStopsBeforeRawCsvIsPublished()
    {
        var directory = Path.Combine(Path.GetTempPath(), "keyload-report-midwrite-" + Guid.NewGuid().ToString("N"));
        var error = new string('x', 4_096);
        var samples = Enumerable.Range(0, 20_000)
        .Select(index => new OperationSample(index, 0, index, index + 1, false, error, 0, null, null)).ToImmutableArray();
        var report = Report() with
        {
            Cases = [new ComparisonCase("large", Scenario.PointRead, 0, "failed", null, null, samples)]
        };
        using var cancellation = new CancellationTokenSource();
        using var observation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task? writing = null;
        Task? watching = null;
        try
        {
            var jsonPath = Path.Combine(directory, "results.json");
            var armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            watching = ObserveJsonGrowthAsync(jsonPath, armed, observation.Token);
            await armed.Task.WaitAsync(observation.Token);
            writing = ReportWriter.WriteAsync(report, directory, cancellation.Token);
            await watching;
            await cancellation.CancelAsync();
            // A write that finishes before observed file growth fails this assertion instead of passing spuriously.
            var stopped = false;
            try
            { await writing; }
            catch (OperationCanceledException) { stopped = true; }
            await Assert.That(stopped).IsTrue();
            await Assert.That(File.Exists(Path.Combine(directory, "samples.csv"))).IsFalse();
        }
        finally
        {
            await observation.CancelAsync();
            await cancellation.CancelAsync();
            await AwaitCanceledOrCompletedAsync(watching);
            await AwaitCanceledOrCompletedAsync(writing);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static async Task AwaitCanceledOrCompletedAsync(Task? operation)
    {
        if (operation is null)
        {
            return;
        }

        try
        { await operation; }
        catch (OperationCanceledException) { }
    }

    private static async Task ObserveJsonGrowthAsync(string path, TaskCompletionSource armed,
        CancellationToken observationToken)
    {
        armed.SetResult();
        while (true)
        {
            observationToken.ThrowIfCancellationRequested();
            if (File.Exists(path) && new FileInfo(path).Length > 0)
            {
                return;
            }
            await Task.Delay(1, observationToken);
        }
    }

    private static ComparisonReport Report()
    {
        var queue = new QueueTimings(1.25, 2.5, 3.75);
        var cases = new[]
        {
            new ComparisonCase("node,\"β\"", Scenario.QueueCycle, 1, "failed", "one timeout", null,
            [
                new OperationSample(0, 2, 1, 2, true, null, 12, "id-α", new(1, 2, 3)),
                new OperationSample(1, 2, 2, 4.5, false, "timeout\n\"Δ\"", 7, "msg,\"β\"", queue)
            ]),
            new ComparisonCase("other", Scenario.PointRead, 0, "failed", "unavailable", null,
            [new OperationSample(2, 0, 4, 5, false, "unavailable", 0, null, null)])
        };
        return new ComparisonReport(3, Guid.Parse("be464719-904b-4ca8-8c14-fb48886f116b"),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), new ComparisonOptions(), "sha", "closed", "linux",
            "x64", 4, ".NET 10", "disk", "revision", ImmutableArray<TargetProfile>.Empty, ImmutableArray.CreateRange(cases));
    }
}
