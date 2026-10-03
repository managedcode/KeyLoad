using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed record ExistingStoreInspectorExit(
    int ProcessId,
    int ExitCode,
    bool Canceled,
    string AssemblySha256,
    string Stdout,
    string Stderr,
    ExistingStoreInspectionReceipt? Receipt,
    bool ProcessReaped,
    bool StdoutReaderSettled,
    bool StderrReaderSettled,
    bool OuterOwnerReleased,
    bool CleanupWarningExceeded);

internal static class ExistingStoreInspectorProcess
{
    private const int SuccessfulExitCode = 0;
    private const int ProtocolErrorExitCode = 2;
    private const int ReceiptCharacterLimit = 8192;
    private const string ReceiptTooLargeMessage = "The inspector receipt exceeded its bounded protocol size.";
    private const string MissingReceiptMessage = "The valid inspector response did not contain its receipt.";

    internal const string ReadyMarker = ExistingStoreInspectorPipeCapture.ReadyMarker;

    internal static Task<ExistingStoreInspectorExit> RunAsync(ExistingStoreInspectionRequest request,
        string outerOwnerPath, bool cancelWhenReady = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var payload = JsonSerializer.Serialize(request, ExistingStoreInspectorProtocol.JsonOptions);
        return ExistingStoreInspectorLifetime.RunAsync(payload, outerOwnerPath, cancelWhenReady, cancellationToken);
    }

    internal static Task<ExistingStoreInspectorExit> RunRawAsync(string payload, string outerOwnerPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return ExistingStoreInspectorLifetime.RunAsync(payload, outerOwnerPath, cancelWhenReady: false, cancellationToken);
    }

    internal static ExistingStoreInspectorExit CreateExit(ExistingStoreInspectorSession session,
        string assemblySha256, bool canceled, bool cleanupWarningExceeded, bool ownerReleased)
    {
        var output = session.Stdout.Text;
        var error = session.Stderr.Text;
        var code = session.Process.ExitCode;
        var receipt = ReadReceipt(output, error, code, canceled, session.Stdout.CharacterCount);
        return new(session.Process.Id, code, canceled, assemblySha256, output, error, receipt,
            session.Process.HasExited, session.StdoutTask.IsCompleted, session.StderrTask.IsCompleted,
            ownerReleased, cleanupWarningExceeded);
    }

    private static ExistingStoreInspectionReceipt? ReadReceipt(string output, string error, int exitCode, bool canceled, long outputCharacters)
    {
        if (canceled)
        {
            if (output.Length != 0)
            {
                throw new InvalidDataException(MissingReceiptMessage);
            }
            return null;
        }
        if (exitCode == ProtocolErrorExitCode)
        {
            if (output.Length != 0 || error.Length != 0)
            {
                throw new InvalidDataException(MissingReceiptMessage);
            }
            return null;
        }
        if (output.Length == 0)
        {
            throw new InvalidDataException(MissingReceiptMessage);
        }
        if (outputCharacters > ReceiptCharacterLimit || output.Length > ReceiptCharacterLimit)
        {
            throw new InvalidDataException(ReceiptTooLargeMessage);
        }
        if (exitCode != SuccessfulExitCode || error.Length != 0)
        {
            throw new InvalidDataException(MissingReceiptMessage);
        }
        return JsonSerializer.Deserialize<ExistingStoreInspectionReceipt>(output, ExistingStoreInspectorProtocol.JsonOptions)
            ?? throw new InvalidDataException(MissingReceiptMessage);
    }
}
