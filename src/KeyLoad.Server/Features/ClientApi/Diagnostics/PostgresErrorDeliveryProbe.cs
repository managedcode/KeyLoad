using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresErrorDeliveryProbe(PostgresErrorDeliveryFiles files,
    IOptions<RequestProbeExecutionOptions> configured, TimeProvider clock)
{
    private readonly Lock sync = new();
    private readonly Dictionary<Guid, PostgresErrorDeliveryClaim> claims = [];

    internal Func<CancellationToken, Task>? BeforeFlush(ServerConnectionFeature owner,
        PostgresSessionIdentity identity, Exception original, CancellationToken closed)
    {
        if (original is not KeyLoadException { Code: ErrorCode.PermissionDenied } primary)
        { return null; }
        lock (sync)
        {
            var arm = files.ReadArm();
            if (arm is null || !StringComparer.Ordinal.Equals(arm.Subject, identity.Subject))
            { return null; }
            if (claims.Count >= configured.Value.MaximumActiveGates || claims.ContainsKey(owner.Id)
                || claims.Values.Any(item => item.Arm == arm.Id) || files.WasClaimed(arm.Id))
            { throw new InvalidOperationException(PostgresErrorDeliveryProtocol.Invalid); }
            var claim = new PostgresErrorDeliveryClaim(arm, owner.Id, primary, files);
            claims.Add(owner.Id, claim);
            return token => HoldAsync(claim, closed, token);
        }
    }

    private async Task HoldAsync(PostgresErrorDeliveryClaim claim, CancellationToken closed, CancellationToken deadline)
    {
        claim.Publish(PostgresErrorDeliveryPhase.Buffered, false, PostgresErrorDeliveryProtocol.None);
        try
        {
            while (true)
            { await Task.Delay(configured.Value.PollInterval, clock, deadline).ConfigureAwait(false); }
        }
        catch (OperationCanceledException) when (closed.IsCancellationRequested)
        { claim.ReceiverClosed(); }
    }

    internal void Ledger(Guid connection, IReadOnlyList<Exception> failures)
    { lock (sync) { if (claims.TryGetValue(connection, out var claim)) { claim.Ledger(failures); } } }

    internal void Session(Guid connection, IReadOnlyList<Exception> failures)
    { lock (sync) { if (claims.TryGetValue(connection, out var claim)) { claim.Session(failures); } } }

    internal void Joined(Guid connection, IReadOnlyList<Exception> failures)
    {
        lock (sync)
        {
            if (!claims.TryGetValue(connection, out var claim))
            { return; }
            try
            { claim.ConnectionJoined(failures); }
            finally { claims.Remove(connection); }
        }
    }
}
