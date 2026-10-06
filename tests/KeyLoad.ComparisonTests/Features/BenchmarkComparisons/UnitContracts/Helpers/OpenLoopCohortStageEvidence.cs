using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KeyLoad.Server;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class OpenLoopCohortStageEvidence
{
    private const string ArtifactsDirectory = "artifacts";
    private const string QualificationDirectory = "qualification";
    private const string DirectoryPrefix = "open-loop-cohort-stages-";
    private const string OutputSuffix = ".stdout.txt";
    private const string ErrorSuffix = ".stderr.txt";
    private const string MetadataSuffix = ".json";
    private const string CaptureLimitMessage = "The actual stage capture exceeded the native output bound.";
    private readonly OpenLoopPlanProcessOptions settings;
    private readonly string directory;

    internal OpenLoopCohortStageEvidence(IOptions<OpenLoopPlanProcessOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(executionOptions);
        ExecutionOptions = executionOptions;
        settings = executionOptions.Value;
        settings.Validate();
        directory = Path.Combine(OpenLoopPlanNodeProcess.RepositoryRoot, ArtifactsDirectory,
            QualificationDirectory, DirectoryPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
    }

    internal IOptions<OpenLoopPlanProcessOptions> ExecutionOptions { get; }

    internal async Task RecordAsync(OpenLoopCohortStage stage, OpenLoopPlanStageObservation actual)
    {
        var failures = new List<Exception>();
        var name = stage.ToString();
        await ServerFailureObserver.ObserveAsync(() => WriteCaptureAsync(name + OutputSuffix, actual.StandardOutput), failures)
            .ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => WriteCaptureAsync(name + ErrorSuffix, actual.StandardError), failures)
            .ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(() => WriteAsync(name + MetadataSuffix,
            JsonSerializer.SerializeToUtf8Bytes(new StageMetadata(stage.ToString(), settings.ProcessTimeout.TotalMilliseconds,
                settings.MaximumOutputCharacters, actual.Started, actual.ProcessId, actual.ExitCode,
                actual.ExecutionMilliseconds, actual.SettlementMilliseconds, actual.CallerCancelledAtExecutionCompletion,
                actual.DeadlineCancelledAtExecutionCompletion, actual.OutputStatus?.ToString(), actual.ErrorStatus?.ToString(),
                actual.OutputExceededBound, actual.ErrorExceededBound, Describe(actual.StandardOutput),
                Describe(actual.StandardError), actual.FailureTypes))), failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private Task WriteCaptureAsync(string name, string? captured)
    {
        if (captured is null)
        {
            return Task.CompletedTask;
        }
        if (captured.Length > settings.MaximumOutputCharacters)
        {
            throw new InvalidOperationException(CaptureLimitMessage);
        }
        return WriteAsync(name, Encoding.UTF8.GetBytes(captured));
    }

    private async Task WriteAsync(string name, byte[] bytes)
    {
        FileStream? stream = null;
        var failures = new List<Exception>();
        async Task WriteOwnedAsync()
        {
            stream = new FileStream(Path.Combine(directory, name), new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                BufferSize = settings.StageEvidenceFileBufferBytes,
                Options = FileOptions.Asynchronous
            });
            await stream.WriteAsync(bytes, CancellationToken.None).ConfigureAwait(false);
            await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        await ServerFailureObserver.ObserveAsync(WriteOwnedAsync, failures).ConfigureAwait(false);
        if (stream is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => stream.DisposeAsync().AsTask(), failures).ConfigureAwait(false);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static CaptureMetadata? Describe(string? captured)
        => captured is null ? null : new(captured.Length,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(captured))));

    private sealed record CaptureMetadata(int Characters, string Sha256);

    private sealed record StageMetadata(string Stage, double ConfiguredDeadlineMilliseconds,
        int MaximumOutputCharacters, bool Started, int? ProcessId, int? ExitCode, double ExecutionMilliseconds,
        double SettlementMilliseconds, bool CallerCancelledAtExecutionCompletion, bool DeadlineCancelledAtExecutionCompletion,
        string? OutputStatus, string? ErrorStatus, bool OutputExceededBound, bool ErrorExceededBound,
        CaptureMetadata? StandardOutput, CaptureMetadata? StandardError, string[] FailureTypes);
}
