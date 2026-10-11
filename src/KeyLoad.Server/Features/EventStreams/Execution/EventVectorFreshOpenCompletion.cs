using KeyLoad.Core;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFreshOpenCompletion
{
    private const string Invalid = "The original event vector completed Open requires its retained terminal outcome.";

    internal static async Task<GrainOperationReply> ExecuteAsync(RemoteDocumentRuntime runtime,
        OrleansNode node, PartitionHost partition, IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock, DecodedGrainRequest parent,
        GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context, ReplicatedOperation originalAdmit, GrainOperationReply actualReply, PhysicalOwnerDirectoryV1 directory,
        string principalId, EventVectorAdmissionPolicy admission, EventVectorRemoteEncoding encoding,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        if (actualReply.Error is not null)
        { return actualReply; }
        work.Check();
        admission.RequireEncodedBytes(actualReply.Payload.Length);
        work.ChargeBytes(actualReply.Payload.Length);
        var value = GrainNativePayload.Read<GrainValue>(actualReply.Payload).Value;
        if (value is EventVectorFirstIssuanceResult)
        { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Invalid); }
        if (value is not EventVectorFirstAdmitAppendResult appended)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var phase = NativeCommandPayload.Read<EventVectorControlPhase>(originalAdmit);
        _ = await EventVectorFirstOpenExecution.ExecuteAfterFreshAdmitAsync(node, runtime, options, limits,
            partition, clock, parent, codec, context, phase, appended, admission, work, cancellationToken).ConfigureAwait(false);
        work.Check();
        var result = await EventVectorParentTerminalObservation.ReadAsync(runtime, node, partition, options.Value,
            phase.OriginalRequest, principalId, directory, parent.Envelope.ExpiresAt, admission, encoding,
            routing, work, cancellationToken).ConfigureAwait(false);
        return result ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
    }
}
