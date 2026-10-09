namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Only an executing real held native context can acknowledge private liveness challenges.</summary>
internal static class RequestCqrsProbeLiveCapture
{
    internal static void Observe(RequestCqrsProbeFiles files, RequestCqrsProbeMarkerRecord marker,
        IGrainContext? context, CancellationToken cancellationToken)
    {
        if (context is null || marker.Phase != RequestCqrsProbePhase.BeforeSubmit
            || context.GrainInstance is not KeyLoad.Orleans.CommandPartitionGrain)
        { return; }
        cancellationToken.ThrowIfCancellationRequested();
        if (context.Deactivated.IsCompleted)
        { throw new InvalidOperationException(RequestCqrsProbeProtocol.InvalidRecord); }
        var witness = RequestCqrsProbeActivationCapture.Create(marker, context);
        files.Live.Acknowledge(witness, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
    }
}
