using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class NativeDiscoveryOmissionOwner
{
    private readonly NativeDiscoveryOmissionFiles files;
    private readonly IHttpContextAccessor accessor;
    private readonly string localVoter;
    internal NativeDiscoveryOmissionOwner(IOptions<NodeOptions> node, IOptions<RequestProbeExecutionOptions> options,
        IHttpContextAccessor accessor)
    {
        if (!node.Value.NativeDiscoveryOmission.Enabled)
        { throw Invalid(); }
        this.accessor = accessor;
        localVoter = node.Value.PublicEndpoint;
        files = new(node, options);
    }
    internal ReplicaDiscoveryProbe CreateBorrowedProbe() => new(BeforeSend, Verified);
    private void BeforeSend(HttpRequestMessage request, CancellationToken token)
    {
        if (!NativeDiscoveryOmissionContext.HasForeground(accessor))
        { return; }
        var target = request.RequestUri?.GetLeftPart(UriPartial.Authority);
        if (target is null || !request.Headers.TryGetValues(ServerProtocol.NonceHeader, out var values)
            || !Guid.TryParseExact(values.Single(), OrleansNodeProtocol.GuidFormat, out var nonce))
        { throw Invalid(); }
        var selected = files.PeekArm();
        if (selected is null || selected.Value.SourceVoter != localVoter || selected.Value.TargetVoter != target)
        { return; }
        files.Locked(() =>
        {
            var arm = files.Arm();
            if (arm is null)
            { return false; }
            var witness = NativeDiscoveryOmissionContext.Read(accessor, arm.Value, localVoter, target, nonce, token);
            if (witness is null)
            { return false; }
            files.Write(NativeDiscoveryOmissionProtocol.RequestFile, witness.Value);
            return true;
        });
    }
    internal byte[] Transform(byte[] payload, ReplicaSiloDiscovery state, HttpContext context, Guid nonce)
    {
        var selected = files.PeekArm();
        if (selected is null || selected.Value.TargetVoter != localVoter)
        { return payload; }
        var source = files.PeekWitness(NativeDiscoveryOmissionProtocol.RequestFile);
        return source is null || source.Value.Nonce != nonce ? payload
            : files.Locked(() => TransformLocked(payload, state, context, nonce));
    }
    private byte[] TransformLocked(byte[] payload, ReplicaSiloDiscovery state, HttpContext context, Guid nonce)
    {
        var arm = files.Arm();
        var source = files.Witness(NativeDiscoveryOmissionProtocol.RequestFile);
        if (arm is null || source is null || source.Value.Nonce != nonce || arm.Value.TargetVoter != localVoter)
        { return payload; }
        context.RequestAborted.ThrowIfCancellationRequested();
        RequireSource(arm.Value, source.Value);
        if (state.VoterId != localVoter || state.RuntimeJournalReaderContract != StoreReaderContract.RuntimeJournal)
        { throw Invalid(); }
        var omitted = NativeDiscoveryCapabilityOmission.Omit(payload, state);
        files.Write(NativeDiscoveryOmissionProtocol.OmittedFile, source.Value with
        { Stage = NativeDiscoveryOmissionProtocol.OmittedStage, ProducerVoter = localVoter, RuntimeJournalReaderContract = StoreReaderContract.Unspecified });
        context.RequestAborted.ThrowIfCancellationRequested();
        return omitted;
    }
    private void Verified(string voter, Guid nonce, ReplicaSiloDiscovery state, CancellationToken token)
    {
        if (!NativeDiscoveryOmissionContext.HasForeground(accessor))
        { return; }
        var selected = files.PeekArm();
        if (selected is null || selected.Value.SourceVoter != localVoter || selected.Value.TargetVoter != voter)
        { return; }
        files.Locked(() =>
        {
            var arm = files.Arm();
            var source = files.Witness(NativeDiscoveryOmissionProtocol.RequestFile);
            if (arm is null || source is null || source.Value.Nonce != nonce || voter != arm.Value.TargetVoter)
            { return false; }
            RequireSource(arm.Value, source.Value);
            var current = NativeDiscoveryOmissionContext.Read(accessor, arm.Value, localVoter, voter, nonce, token);
            var omitted = files.Witness(NativeDiscoveryOmissionProtocol.OmittedFile);
            if (current != source || omitted != (source.Value with
            { Stage = NativeDiscoveryOmissionProtocol.OmittedStage, ProducerVoter = voter, RuntimeJournalReaderContract = StoreReaderContract.Unspecified })
                || state.RuntimeJournalReaderContract != StoreReaderContract.Unspecified)
            { throw Invalid(); }
            files.Write(NativeDiscoveryOmissionProtocol.VerifiedFile, source.Value with
            { Stage = NativeDiscoveryOmissionProtocol.VerifiedStage, ProducerVoter = state.VoterId, RuntimeJournalReaderContract = StoreReaderContract.Unspecified });
            return true;
        });
    }
    private static void RequireSource(NativeDiscoveryOmissionArm arm, NativeDiscoveryOmissionWitness source)
    {
        if (source.ArmId != arm.ArmId || source.SessionId != arm.SessionId || source.SourceVoter != arm.SourceVoter
            || source.TargetVoter != arm.TargetVoter || source.Route != arm.Route
            || source.Stage != NativeDiscoveryOmissionProtocol.SourceStage || source.CommandId != Guid.Empty
            || source.RuntimeJournalReaderContract is not null || source.ProducerVoter is not null)
        { throw Invalid(); }
    }
    private static InvalidOperationException Invalid() => new(NativeDiscoveryOmissionProtocol.Invalid);
}
