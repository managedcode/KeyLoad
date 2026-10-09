using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class ConnectionProbeCapture(RequestCqrsProbeFiles files,
    IOptions<RequestProbeExecutionOptions> options) : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly Dictionary<Guid, Task> watchers = [];
    private readonly CancellationTokenSource stopping = new();

    internal async Task ObserveAsync(RequestCqrsProbeMarkerRecord marker, IGrainContext context,
        CancellationToken cancellationToken)
    {
        if (marker.Phase != RequestCqrsProbePhase.RequestStarted
            || marker.Outcome != RequestCqrsProbeOutcome.Observed || context.GrainInstance is not ConnectionGrain grain)
        { return; }
        files.RequireActiveArm(files.ReadSnapshot().Arms.Single(arm => arm.Record.ArmId == marker.ArmId));
        var management = context.ActivationServices.GetRequiredService<IGrainFactory>()
            .GetGrain<IManagementGrain>(ConnectionProbeProtocol.ManagementGrainKey);
        var active = await management.GetActiveGrains(GrainType.Create(GrainRoutingProtocol.RequestAlias),
            cancellationToken).ConfigureAwait(true);
        var witness = new ConnectionProbeWitness(marker.Version, marker.SessionId, marker.ArmId, marker.RequestId,
            marker.CommandId, marker.Voter, marker.SiloAddress, grain.GetPrimaryKey(), GrainRoutingProtocol.RequestAlias,
            context.GrainId.ToString(), context.ActivationId.ToParsableString(),
            active.Count(identity => identity == context.GrainId), active.Count, Closed: false);
        files.WriteConnection(witness);
        lock (gate)
        {
            if (watchers.Count >= options.Value.MaximumArms || watchers.ContainsKey(marker.ArmId))
            { throw new InvalidOperationException(ConnectionProbeProtocol.Invalid); }
            watchers.Add(marker.ArmId, Task.Run(() => ObserveClosedAsync(context, management, witness), stopping.Token));
        }
    }

    private async Task ObserveClosedAsync(IGrainContext context, IManagementGrain management,
        ConnectionProbeWitness witness)
    {
        try
        {
            await context.Deactivated.WaitAsync(stopping.Token).ConfigureAwait(false);
            RequestContext.Clear();
            using var timeout = new CancellationTokenSource(options.Value.HoldTimeout, TimeProvider.System);
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token, timeout.Token);
            var token = bounded.Token;
            var selected = context.GrainId;
            List<GrainId> active;
            do
            {
                active = await management.GetActiveGrains(GrainType.Create(GrainRoutingProtocol.RequestAlias), token)
                    .ConfigureAwait(false);
                if (active.Contains(selected))
                { await Task.Delay(options.Value.PollInterval, token).ConfigureAwait(false); }
            } while (active.Contains(selected));
            files.WriteConnection(witness with
            { SelectedActivationCount = ConnectionProbeProtocol.Absent,
                ClusterConnectionActivationCount = active.Count, Closed = true });
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        { }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync().ConfigureAwait(false);
        Task[] pending;
        lock (gate) { pending = watchers.Values.ToArray(); }
        try
        { await Task.WhenAll(pending).ConfigureAwait(false); }
        finally
        { stopping.Dispose(); }
    }
}
