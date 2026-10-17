using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Composes native observations under the original callback, gate and file owners.</summary>
internal sealed class RequestCqrsNativeObservations
{
    internal RequestCqrsNativeModelObservation Model
    {
        get;
    }
    internal RequestCqrsNativeReplayObservation Replay
    {
        get;
    }
    internal RequestCqrsControlledLifetimeObservation Controlled
    {
        get;
    }

    internal RequestCqrsNativeObservations(RequestCqrsProbeFiles files, RequestCqrsProbeLifecycle lifecycle,
        IOptions<RequestProbeExecutionOptions> options, TimeProvider clock, string voter, string address,
        Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> marker,
        Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, IGrainContext?, CancellationToken, Task> hold,
        CancellationToken stopping, CancellationToken applicationStopping)
    {
        Model = new(files, lifecycle, options, voter, address, marker, hold);
        Replay = new(files, lifecycle, options, voter, address, marker, hold);
        Controlled = new(files, lifecycle, options, clock, voter, address, marker, stopping, applicationStopping);
    }
}
