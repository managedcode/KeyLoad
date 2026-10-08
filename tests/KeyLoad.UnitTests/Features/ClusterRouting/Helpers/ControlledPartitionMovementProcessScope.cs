using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Owns exact phase admission and joined process handoff, with no old-engine admission reuse.</summary>
internal sealed class ControlledPartitionMovementProcessScope : IDisposable
{
    private const int FirstOrdinal = 0;
    private static readonly AsyncLocal<ControlledPartitionMovementProcessScope?> Active = new();
    private readonly ControlledPartitionMovementNode source;
    private readonly ControlledPartitionMovementNode target;
    private readonly ControlledPartitionMovementLoopbackListeners listeners;
    private readonly PartitionMovePeerStage stage;
    private readonly CommitStage cut;
    private ControlledPartitionMovementCaptureRuntime? closedSource;

    internal ControlledPartitionMovementProcessScope(ControlledPartitionMovementNode source,
        ControlledPartitionMovementNode target, ControlledPartitionMovementLoopbackListeners listeners,
        PartitionMovePeerStage stage, CommitStage cut)
    {
        if (Active.Value is not null)
        { throw new InvalidOperationException("A process phase is already owned."); }
        this.source = source;
        this.target = target;
        this.listeners = listeners;
        this.stage = stage;
        this.cut = cut;
        Active.Value = this;
    }

    internal static ControlledPartitionMovementProcessScope? Current => Active.Value;
    internal bool Exercised { get; private set; }
    internal void RegisterClosedSource(ControlledPartitionMovementCaptureRuntime owner) => closedSource = owner;

    internal static Task<PartitionMovementTransportRequest> VerifyAsync(ControlledPartitionMovementNode node,
        ServerRuntimeOptions runtime, PartitionMovementTransportRequest original, CancellationToken token)
        => ControlledPartitionMovementFreshAdmission.VerifyAsync(node, runtime, original, token);

    internal async Task<OperationResult> SubmitAsync(ControlledPartitionMovementNode node,
        ServerRuntimeOptions runtime, PartitionMovementTransportRequest original, CancellationToken token)
    {
        var verified = await VerifyAsync(node, runtime, original, token);
        if (verified.Action != PartitionMovementTransportAction.Apply)
        { throw new InvalidOperationException("Only an admitted original logged phase is submitted."); }
        var principalId = PartitionMovementControlPrincipal.Resolve(node.Database, verified.Envelope);
        var principal = node.Store.Read(view => node.Database.Principal(view, principalId,
            node.Database.EvaluationClock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        var operation = node.Database.CreateVerifiedPartitionMovementOperation(verified.CommandId,
            principal.Id, node.Database.EvaluationClock.GetUtcNow(), verified.Envelope);
        if (Exercised || verified.Envelope.Stage != stage || verified.Envelope.PageOrdinal != FirstOrdinal
            || stage == PartitionMovePeerStage.Abort && !ReferenceEquals(node, source))
        { return node.Journal.Submit(operation, token); }
        if (stage == PartitionMovePeerStage.Abort)
        {
            var joined = closedSource ?? throw new InvalidOperationException("The source abort owner is not joined.");
            await joined.DisposeAsync();
        }
        Exercised = true;
        return await ControlledPartitionMovementTerminalProcessHandoff.ExecuteAsync(source, target,
            listeners, node, operation, cut, token);
    }

    public void Dispose() => Active.Value = null;
}
