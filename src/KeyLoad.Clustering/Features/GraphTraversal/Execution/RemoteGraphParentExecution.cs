using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static class RemoteGraphParentExecution
{
    internal static async Task<GrainOperationReply> ExecuteAsync(DecodedGrainRequest original,
        IServiceProvider services, TimeProvider clock, GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context,
        ICqrsStreamWriter<GrainRequestProgress, GrainOperationReply> writer, IResourceLifetimeWorkBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        var token = writer.CancellationToken;
        token.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        codec.ValidateScope(original.Envelope);
        if (original.Envelope.Purpose != RemoteGraphRequestScope.ParentPurpose)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, GrainRoutingProtocol.InvalidRequest);
        }
        var database = services.GetRequiredService<DatabaseEngine>();
        _ = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        var parent = services.GetService<IRemoteGraphParent>()
            ?? throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest);
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, token);
        work.AttachResourceLifetimeBinding(binding);
        work.ConstrainLifetime(original.Envelope.ExpiresAt);
        if (original.Envelope.ReadKind == GrainReadKind.RemoteGraphIncomingParent)
        {
            var request = GrainNativePayload.ReadPublicInput<ReadRemoteIncomingGraphEdgesRequestV1>(original.Payload);
            var result = await parent.ReadIncomingAsync(original.Envelope, request, work, token, binding).ConfigureAwait(true);
            work.CheckResult(result);
            return GrainReplyFactory.Value(result, codec.ExecutionOptions, token);
        }
        var kind = original.Envelope.CommandKind
            ?? throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        if (!RemoteGraphRequestScope.HandlesParent(kind))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, GrainRoutingProtocol.InvalidRequest);
        }
        var command = await parent.ExecuteAsync(original.Envelope, kind, original.Payload, work, token, binding).ConfigureAwait(true);
        work.CheckResult(command);
        return GrainReplyFactory.Value(command, codec.ExecutionOptions, token);
    }
}
