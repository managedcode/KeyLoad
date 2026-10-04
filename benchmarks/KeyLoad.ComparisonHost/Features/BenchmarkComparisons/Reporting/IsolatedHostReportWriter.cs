using System.Text.Json;
using KeyLoad.Comparisons;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Publishes one streamed envelope using a same-directory atomic no-overwrite move.</summary>
internal static class IsolatedHostReportWriter
{
    internal static void ValidateDestination(string directory)
    {
        var final = Path.Combine(directory, IsolatedHostConstants.WorkerFile);
        if (File.Exists(final) || Directory.Exists(final))
        {
            throw new IOException(IsolatedHostConstants.Failure);
        }
    }

    internal static async Task WriteAsync(IsolatedComparisonReport report, string directory, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ValidateDestination(directory);
        Directory.CreateDirectory(directory);
        var pending = Path.Combine(directory, IsolatedHostConstants.PendingPrefix + Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat)
            + IsolatedHostConstants.PendingSuffix);
        var created = false;
        try
        {
            await using (var stream = CreatePending(pending))
            {
                created = true;
                await WritePendingAsync(report, stream, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, Path.Combine(directory, IsolatedHostConstants.WorkerFile), overwrite: false);
        }
        finally
        {
            if (created && File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private static FileStream CreatePending(string pending)
        => new(pending, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = IsolatedHostConstants.FileBufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

    private static async Task WritePendingAsync(IsolatedComparisonReport report, Stream stream, CancellationToken cancellationToken)
    {
        var value = new IsolatedHostStreamedEnvelope(report.SchemaVersion, report.Worker, report.Disposition, report.Reason,
            report.Report is { } measured ? StreamedComparisonReport.Create(measured) : null);
        await JsonSerializer.SerializeAsync(stream, value, ReportWriter.JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record IsolatedHostStreamedEnvelope(int SchemaVersion, IsolatedComparisonWorker Worker,
        string Disposition, string? Reason, StreamedComparisonReport? Report);
}
