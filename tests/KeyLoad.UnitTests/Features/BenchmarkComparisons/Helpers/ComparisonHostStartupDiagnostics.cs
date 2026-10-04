using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Projects startup timeout observations into a closed, privacy-safe receipt.</summary>
internal sealed record ComparisonHostStartupDiagnostics(ComparisonHostStartupStage Stage,
    ComparisonHostProcessExitState ProcessExitState, int? ExitCode,
    ComparisonHostOutputCaptureSnapshot Stdout, ComparisonHostOutputCaptureSnapshot Stderr)
{
    private const string TimeoutMessage = "The comparison host did not exit within its startup deadline.";
    private const string UnavailableReceipt = "unavailable";
    internal const int MaximumReceiptUtf8Bytes = 8_192;

    private static readonly JsonSerializerOptions ReceiptOptions = CreateReceiptOptions();

    internal const string TimeoutPrefix = TimeoutMessage + " Diagnostic: ";

    internal static ComparisonHostStartupDiagnostics Create(ComparisonHostStartupStage stage, Process process,
        ComparisonHostOutputCaptureSnapshot stdout, ComparisonHostOutputCaptureSnapshot stderr)
    {
        ArgumentNullException.ThrowIfNull(process);
        ComparisonHostProcessExitState exitState;
        int? exitCode = null;
        try
        {
            var hasExited = process.HasExited;
            exitState = hasExited ? ComparisonHostProcessExitState.Exited : ComparisonHostProcessExitState.Running;
            if (hasExited)
            {
                exitCode = process.ExitCode;
            }
        }
        catch (InvalidOperationException)
        {
            exitState = ComparisonHostProcessExitState.Unavailable;
        }
        catch (Win32Exception)
        {
            exitState = ComparisonHostProcessExitState.Unavailable;
        }
        catch (NotSupportedException)
        {
            exitState = ComparisonHostProcessExitState.Unavailable;
        }

        return new(stage, exitState, exitCode, stdout, stderr);
    }

    internal static TimeoutException CreateTimeoutException(ComparisonHostStartupDiagnostics diagnostics)
    {
        try
        {
            var receipt = Project(diagnostics);
            var json = JsonSerializer.Serialize(receipt, ReceiptOptions);
            var message = TimeoutPrefix + json;
            return Encoding.UTF8.GetByteCount(message) <= MaximumReceiptUtf8Bytes
                ? new(message)
                : UnavailableTimeout();
        }
        catch (JsonException)
        {
            return UnavailableTimeout();
        }
        catch (InvalidOperationException)
        {
            return UnavailableTimeout();
        }
        catch (NotSupportedException)
        {
            return UnavailableTimeout();
        }
    }

    private static TimeoutException UnavailableTimeout() => new(TimeoutPrefix + UnavailableReceipt);

    private static SafeReceipt Project(ComparisonHostStartupDiagnostics diagnostics)
    {
        var stage = diagnostics.Stage switch
        {
            ComparisonHostStartupStage.ProcessExit => ComparisonHostStartupStage.ProcessExit,
            ComparisonHostStartupStage.OutputDrain => ComparisonHostStartupStage.OutputDrain,
            _ => ComparisonHostStartupStage.Unavailable
        };
        var exitState = diagnostics.ProcessExitState switch
        {
            ComparisonHostProcessExitState.Running => ComparisonHostProcessExitState.Running,
            ComparisonHostProcessExitState.Exited => ComparisonHostProcessExitState.Exited,
            _ => ComparisonHostProcessExitState.Unavailable
        };
        var stdout = ProjectCapture(diagnostics.Stdout);
        var stderr = ProjectCapture(diagnostics.Stderr);
        return new(stage, exitState, exitState == ComparisonHostProcessExitState.Exited ? diagnostics.ExitCode : null,
            stdout.State, stdout.Length, stderr.State, stderr.Length,
            stdout.MissingKeyLoadSetting || stderr.MissingKeyLoadSetting,
            stdout.MissingQdrantSetting || stderr.MissingQdrantSetting,
            stdout.InvalidDimensions || stderr.InvalidDimensions);
    }

    private static ComparisonHostOutputCaptureSnapshot ProjectCapture(ComparisonHostOutputCaptureSnapshot snapshot)
    {
        return !Enum.IsDefined(snapshot.State) || snapshot.Length < 0 ||
            snapshot.Length > ComparisonHostOutputCapture.MaximumCapturedCharacters ||
            snapshot.State == ComparisonHostCaptureState.Unavailable
                ? ComparisonHostOutputCaptureSnapshot.Unavailable
                : snapshot;
    }

    private static JsonSerializerOptions CreateReceiptOptions()
    {
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed record SafeReceipt(ComparisonHostStartupStage Stage,
        ComparisonHostProcessExitState ProcessExitState, int? ExitCode,
        ComparisonHostCaptureState StdoutState, int StdoutLength,
        ComparisonHostCaptureState StderrState, int StderrLength,
        bool MissingKeyLoadSetting, bool MissingQdrantSetting, bool InvalidDimensions);
}

internal enum ComparisonHostStartupStage
{
    Unavailable,
    ProcessExit,
    OutputDrain
}

internal enum ComparisonHostProcessExitState
{
    Unavailable,
    Running,
    Exited
}
