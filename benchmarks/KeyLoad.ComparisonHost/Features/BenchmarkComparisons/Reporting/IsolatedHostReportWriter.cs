using System.Text.Json;
using KeyLoad.Comparisons;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonHost.Features.BenchmarkComparisons;

/// <summary>Publishes one streamed envelope using a same-directory atomic no-overwrite move.</summary>
internal static class IsolatedHostReportWriter
{
    internal static void ValidateDestination(string directory, string fileName = IsolatedHostConstants.WorkerFile)
    {
        var final = Path.Combine(directory, fileName);
        if (File.Exists(final) || Directory.Exists(final))
        {
            throw new IOException(IsolatedHostConstants.Failure);
        }
    }

    internal static async Task WriteAsync(IsolatedComparisonReport report, string directory,
        IOptions<ComparisonHostExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        var value = new IsolatedHostStreamedEnvelope(report.SchemaVersion, report.Worker, report.Disposition, report.Reason,
            report.Report is { } measured ? StreamedComparisonReport.Create(measured) : null);
        await WriteEnvelopeAsync(value, directory, IsolatedHostConstants.WorkerFile, executionOptions, cancellationToken);
    }

    internal static async Task WriteEnvelopeAsync<T>(T report, string directory, string fileName,
        IOptions<ComparisonHostExecutionOptions> executionOptions, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!executionOptions.Value.IsValid())
        { throw new OptionsValidationException(Options.DefaultName, typeof(ComparisonHostExecutionOptions), [ComparisonHostExecutionOptions.InvalidSettings]); }
        ValidateDestination(directory, fileName);
        Directory.CreateDirectory(directory);
        var pending = Path.Combine(directory, IsolatedHostConstants.PendingPrefix + Guid.NewGuid().ToString(ComparisonHostConstants.GuidFormat)
            + IsolatedHostConstants.PendingSuffix);
        var created = false;
        try
        {
            await using (var stream = CreatePending(pending, executionOptions))
            {
                created = true;
                await WritePendingAsync(report, stream, cancellationToken);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(pending, Path.Combine(directory, fileName), overwrite: false);
        }
        finally
        {
            if (created && File.Exists(pending))
            {
                File.Delete(pending);
            }
        }
    }

    private static FileStream CreatePending(string pending, IOptions<ComparisonHostExecutionOptions> executionOptions)
        => new(pending, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            BufferSize = executionOptions.Value.FileBufferBytes,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan
        });

    private static async Task WritePendingAsync<T>(T report, Stream stream, CancellationToken cancellationToken)
    {
        await JsonSerializer.SerializeAsync(stream, report, ReportWriter.JsonOptions, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private sealed record IsolatedHostStreamedEnvelope(int SchemaVersion, IsolatedComparisonWorker Worker,
        string Disposition, string? Reason, StreamedComparisonReport? Report);
}
