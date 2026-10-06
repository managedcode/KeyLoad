using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-CQ-034: configured native buffers preserve the exact CSV report contract.</summary>
internal sealed class NativeComparisonReportPolicyTests
{
    [Test]
    public async Task DefaultAndConfiguredLowerNativeBuffersWriteIdenticalGoldenCsvBytesAsync()
    {
        const string header = "target,scenario,repetition,operation,worker,started_ms,completed_ms,latency_ms,success,error,payload_bytes,completed_message_id,enqueue_ms,receive_ms,ack_ms\n";
        const string row = "\"node,\"\"β\"\"\",QueueCycle,1,1,2,2.000,4.500,2.500,False,\"timeout\n\"\"Δ\"\"\",7,\"msg,\"\"β\"\"\",1.250,2.500,3.750";
        var expected = Encoding.UTF8.GetBytes(header + row + Environment.NewLine);
        var report = CreateReport();
        var directory = Path.Combine(Path.GetTempPath(), "keyload-native-csv-policy-" + Guid.NewGuid().ToString("N"));
        var configured = UnitBenchmarkOptions.Native();
        configured.Value.ReportFileBufferBytes = 64;
        configured.Value.ReportWriterBufferCharacters = 128;
        configured.Value.Validate();
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        try
        {
            Directory.CreateDirectory(directory);
            var defaultPath = Path.Combine(directory, "default.csv");
            var configuredPath = Path.Combine(directory, "configured.csv");
            await ReportCsvWriter.WriteAsync(report, defaultPath, Number, UnitBenchmarkOptions.Native(), cancellationToken);
            await ReportCsvWriter.WriteAsync(report, configuredPath, Number, configured, cancellationToken);
            var original = await File.ReadAllBytesAsync(defaultPath, cancellationToken);
            var overridden = await File.ReadAllBytesAsync(configuredPath, cancellationToken);
            await Assert.That(original.SequenceEqual(expected)).IsTrue();
            await Assert.That(overridden.SequenceEqual(expected)).IsTrue();
            await Assert.That(overridden.SequenceEqual(original)).IsTrue();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    private static string Number(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty;

    private static ComparisonReport CreateReport()
    {
        var sample = new OperationSample(1, 2, 2, 4.5, false, "timeout\n\"Δ\"", 7, "msg,\"β\"", new(1.25, 2.5, 3.75));
        var item = new ComparisonCase("node,\"β\"", Scenario.QueueCycle, 1, "failed", "one timeout", null, [sample]);
        return new ComparisonReport(3, new Guid("be464719-904b-4ca8-8c14-fb48886f116b"),
            new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero), new ComparisonOptions(), "sha", "closed", "linux",
            "x64", 4, ".NET 10", "disk", "revision", ImmutableArray<TargetProfile>.Empty, [item]);
    }
}
