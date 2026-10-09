using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

/// <summary>Runs the original hold in the actual native callback with one captured validated options owner.</summary>
internal sealed class RequestCqrsProbeHeldCallback
{
    private readonly RequestCqrsProbeFiles files;
    private readonly IHostApplicationLifetime applicationLifetime;
    private readonly RequestProbeExecutionOptions settings;
    private readonly TimeProvider clock;
    private readonly CancellationToken stopping;
    private readonly Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> createMarker;
    internal RequestCqrsProbeHeldCallback(RequestCqrsProbeFiles files, IHostApplicationLifetime applicationLifetime,
        IOptions<RequestProbeExecutionOptions> options, TimeProvider clock,
        Func<RequestCqrsProbeClaim, RequestCqrsProbePhase, RequestCqrsProbeOutcome, RequestCqrsProbeMarkerRecord> createMarker,
        CancellationToken stopping)
    {
        this.files = files;
        this.applicationLifetime = applicationLifetime;
        settings = options.Value;
        this.clock = clock;
        this.createMarker = createMarker;
        this.stopping = stopping;
    }
    internal async Task RunAsync(RequestCqrsProbeClaim claim, RequestCqrsProbePhase phase,
        IGrainContext? context, CancellationToken requestCancellation)
    {
        const int ReleasesLengthValidationBoundary = 1;
        const int EmptyReleasesLength = 1;
        const int ReleasesFirstIndex = 0;

        var holdTimeout = settings.HoldTimeout;
        var pollInterval = settings.PollInterval;
        using var ceiling = new CancellationTokenSource(holdTimeout, clock);
        using var hostStop = CancellationTokenSource.CreateLinkedTokenSource(applicationLifetime.ApplicationStopping, stopping);
        using var request = CancellationTokenSource.CreateLinkedTokenSource(requestCancellation, hostStop.Token);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(request.Token, ceiling.Token);
        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                var snapshot = files.ReadSnapshot();
                RequestCqrsProbeFiles.RequireActiveArm(claim.Arm, snapshot);
                RequestCqrsProbeLiveCapture.Observe(files, createMarker(claim, phase, RequestCqrsProbeOutcome.Observed), context, linked.Token);
                var releases = snapshot.Releases.Where(record => record.ArmId == claim.Arm.Record.ArmId).ToArray();
                if (releases.Length > ReleasesLengthValidationBoundary || releases.Length == EmptyReleasesLength
                    && (releases[ReleasesFirstIndex].RequestId != claim.Identity.RequestId || releases[ReleasesFirstIndex].SessionId != claim.Arm.Record.SessionId))
                { throw Invalid(); }
                if (releases.Length == EmptyReleasesLength)
                {
                    files.WriteMarker(createMarker(claim, phase, RequestCqrsProbeOutcome.Released));
                    return;
                }
                await Task.Delay(pollInterval, clock, linked.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException cancellation) when (linked.IsCancellationRequested)
        {
            try
            { files.WriteMarker(createMarker(claim, phase, RequestCqrsProbeOutcome.Cancelled)); }
            catch (Exception cleanup) when (NativeCqrsBoundaryErrors.IsNonFatal(cleanup))
            { throw new AggregateException(cancellation, cleanup); }
            throw;
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeProtocol.InvalidFiles);
}
