using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace KeyLoad.Artifacts;

/// <summary>Captures and records every observed failure through a typed aggregate envelope.</summary>
internal static class BackupArtifactFailurePolicy
{
    internal const string CaptureEnvelopeMessage = "A guarded archive operation failed.";

    internal static bool TryCapture(Action operation, Action<Exception> record)
    {
        try
        {
            Invoke(operation);
            return true;
        }
        catch (AggregateException envelope)
        {
            RecordEnvelope(envelope, record);
            return false;
        }
    }

    internal static void Invoke(Action operation)
    {
        try
        {
            operation();
        }
        catch (Exception failure)
        {
            throw new AggregateException(CaptureEnvelopeMessage, failure);
        }
    }

    internal static void RecordEnvelope(AggregateException envelope, Action<Exception> record)
    {
        foreach (var failure in envelope.InnerExceptions)
        {
            record(failure);
        }
    }

    internal static Exception Combine(Exception? primary, Exception cleanup)
        => primary is null ? cleanup : new AggregateException(primary, cleanup);

    [DoesNotReturn]
    internal static void Throw(Exception failure) => ExceptionDispatchInfo.Capture(failure).Throw();
}
