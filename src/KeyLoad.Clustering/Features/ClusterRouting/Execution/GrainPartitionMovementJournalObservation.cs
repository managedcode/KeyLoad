namespace KeyLoad.Clustering;

internal static class GrainPartitionMovementJournalObservation
{
    internal static void CaptureReturned(GrainRequestCodec? codec, DecodedGrainRequest request,
        OperationResult actual, global::Orleans.Runtime.IGrainContext? context)
    {
        if (request.Envelope.CommandKind != OperationKind.PartitionMovementPhase
            || codec?.PhaseObserver is not IGrainPartitionMovementJournalObserver observer)
        {
            return;
        }
        ArgumentNullException.ThrowIfNull(context);
        observer.CaptureReturned(GrainRequestProbeIdentity.From(request.Envelope), actual, context);
    }
}
