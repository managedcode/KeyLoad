using System.Text.Json;
using Grpc.Core;
using KurrentDB.Client;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentCleanupDiagnostics
{
    internal const int MaximumCharacters = 4096;
    private const int MaximumExceptionDepth = 8, MaximumGrpcStatus = 16;
    private const string Prefix = "KurrentCleanupDiagnostic ";

    internal static string Project(KurrentCleanupDiagnostic diagnostic)
    {
        ArgumentNullException.ThrowIfNull(diagnostic);
        if (diagnostic.SchemaVersion != 1 || !diagnostic.Counts.IsValid || diagnostic.ElapsedMilliseconds < 0 || diagnostic.LaterDisposalFailures < 0
            || !Enum.IsDefined(diagnostic.Stage) || !Enum.IsDefined(diagnostic.Outcome) || !Enum.IsDefined(diagnostic.Reason)
            || diagnostic.GrpcStatus is < 0 or > MaximumGrpcStatus || InvalidSuccess(diagnostic))
        {
            throw new ArgumentException(KurrentConstants.CleanupInvalidDiagnostic, nameof(diagnostic));
        }
        var json = JsonSerializer.Serialize(diagnostic);
        return json.Length + Prefix.Length + Environment.NewLine.Length <= MaximumCharacters
            ? json : throw new InvalidOperationException(KurrentConstants.CleanupInvalidDiagnostic);
    }

    private static bool InvalidSuccess(KurrentCleanupDiagnostic diagnostic)
        => diagnostic.Outcome == KurrentCleanupOutcome.Succeeded && (!diagnostic.Counts.IsComplete
            || diagnostic.Stage != KurrentCleanupStage.Complete || diagnostic.Reason != KurrentCleanupFailureReason.None
            || diagnostic.GrpcStatus is not null || diagnostic.LaterDisposalFailures != 0 || diagnostic.DeadlineExpired);

    internal static (KurrentCleanupFailureReason Reason, int? GrpcStatus) Classify(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        for (var depth = 0; depth < MaximumExceptionDepth; depth++)
        {
            if (error is RpcException rpc)
            {
                return (KurrentCleanupFailureReason.NativeRpc, NativeStatus(rpc));
            }
            if (error is OperationCanceledException)
            {
                return (KurrentCleanupFailureReason.Cancelled, null);
            }
            if (error is AccessDeniedException or NotAuthenticatedException or NotLeaderException
                or WrongExpectedVersionException or StreamDeletedException or StreamNotFoundException)
            {
                return (KurrentCleanupFailureReason.NativeFailure, error.InnerException is RpcException nativeRpc ? NativeStatus(nativeRpc) : null);
            }
            if (error.InnerException is not { } inner)
            {
                break;
            }
            error = inner;
        }
        return (KurrentCleanupFailureReason.Unknown, null);
    }

    private static int? NativeStatus(RpcException error)
    {
        var status = (int)error.StatusCode;
        return status is >= 0 and <= MaximumGrpcStatus ? status : null;
    }

    internal static void WriteFinal(KurrentCleanupDiagnostic diagnostic)
    {
        try
        {
            Console.Error.WriteLine(Prefix + Project(diagnostic));
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or ArgumentException or NotSupportedException or JsonException)
        {
            // Diagnostic serialization or a broken pipe cannot replace the original cleanup outcome.
        }
    }
}
