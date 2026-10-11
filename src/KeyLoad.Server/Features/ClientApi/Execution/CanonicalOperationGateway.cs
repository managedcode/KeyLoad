using KeyLoad.Diagnostics.Features.ResourceExecution;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares authenticated signed request-actor dispatch across public protocol adapters.</summary>
internal static class CanonicalOperationGateway
{
    /// <summary>Executes one operation using the current persisted middleware principal.</summary>
    /// <param name="context">The current authenticated HTTP request, never a retained session.</param>
    /// <param name="readKind">The read capability, or null for a command.</param>
    /// <param name="commandKind">The command capability, or null for a read.</param>
    /// <param name="commandId">The caller's stable write identity, empty for reads.</param>
    /// <param name="payload">The canonical typed payload.</param>
    /// <param name="cancellationToken">Cancels the caller's wait without asserting write rollback.</param>
    /// <returns>The exact canonical reply and actual execution identity.</returns>
    internal static Task<DispatchedOperationReply> ExecuteAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
        => ExecuteCoreAsync(context, readKind, commandKind, commandId, payload, null, cancellationToken);

    internal static Task<DispatchedOperationReply> ExecuteTailWithProgressAsync(HttpContext context,
        Guid commandId, ReadOnlyMemory<byte> payload, Func<GrainRequestProgress, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken)
        => ExecuteCoreAsync(context, null, OperationKind.EventFeedControl, commandId, payload, progress, cancellationToken);

    private static async Task<DispatchedOperationReply> ExecuteCoreAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress, CancellationToken cancellationToken)
    {
        var execution = await ExecuteNativeAsync(context, readKind, commandKind, commandId, payload,
            progress, cancellationToken).ConfigureAwait(false);
        var requestId = execution.RequestId;
        var reply = execution.Reply;
        try
        {
            var value = NativeSerialization.Deserialize<GrainValue>(reply.Payload.Span);
            cancellationToken.ThrowIfCancellationRequested();
            return new(requestId, McpBoundedJson.Serialize(PublicReadResponse.Create(readKind, commandKind, value.Value),
                context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>().Value.MaximumDataReplyBytes));
        }
        catch (Exception failure) when (commandKind is OperationKind.ReceiveAcrossLanes or OperationKind.MaintainAnnIndex or OperationKind.MaintainTextIndex or OperationKind.MaintainOnlineTextIndex or OperationKind.MovePartition
            && failure is KeyLoadException or System.Text.Json.JsonException or ArgumentException)
        {
            GrainFailureDiagnostics.Log(context.RequestServices.GetRequiredService<ILogger<OrleansNode>>(),
                failure, requestId, GrainFailureStage.ReplyEncoding, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Interrupted(commandKind));
        }
    }

    internal static async Task<EventFeedControlResult> ExecuteTailResultAsync(HttpContext context, Guid commandId,
        ReadOnlyMemory<byte> payload, Func<GrainRequestProgress, CancellationToken, ValueTask> progress,
        CancellationToken cancellationToken)
    {
        var execution = await ExecuteNativeAsync(context, null, OperationKind.EventFeedControl, commandId,
            payload, progress, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return NativeSerialization.Deserialize<GrainValue>(execution.Reply.Payload.Span).Value is EventFeedControlResult value
            ? value : throw Errors.Fail(ErrorCode.RecoveryRequired, GrainRoutingProtocol.InvalidRequest);
    }

    private static async Task<NativeDispatch> ExecuteNativeAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload,
        Func<GrainRequestProgress, CancellationToken, ValueTask>? progress, CancellationToken cancellationToken)
    {
        var actual = CanonicalOperationRequestPreparation.Create(context, readKind, commandKind,
            commandId, payload, cancellationToken);
        if (progress is not null && !actual.ReceivingOnly)
        { throw Errors.Fail(ErrorCode.Validation, GrainRoutingProtocol.InvalidRequest); }
        var requestId = actual.RequestId;
        var started = DatabasePhaseTelemetry.Begin();
        var outcome = DatabasePhaseOutcome.Faulted;
        GrainOperationReply reply;
        try
        {
            var node = context.RequestServices.GetRequiredService<OrleansNode>();
            using var connection = ServerConnectionFeature.Acquire(context, cancellationToken);
            using var requestContext = node.OpenRequestContext(actual.Principal, requestId,
                commandId, connection.Token, connection.Id);
            reply = progress is null
                ? await node.ExecuteAsync(requestId, actual.SignedRequest, actual.Command, connection.Token).ConfigureAwait(false)
                : await node.ExecuteTailWithProgressAsync(requestId, actual.SignedRequest, progress, connection.Token).ConfigureAwait(false);
            outcome = DatabasePhaseOutcome.Completed;
        }
        catch (Exception failure) when (failure is OperationCanceledException or KeyLoadException { Code: ErrorCode.Cancelled })
        {
            outcome = DatabasePhaseTelemetry.CancellationOutcome(cancellationToken);
            throw;
        }
        finally
        {
            DatabasePhaseTelemetry.End(DatabasePhaseKind.PublicOperationDispatch, outcome, started);
        }
        global::KeyLoad.Server.Features.EventStreams.EventVectorGatewaySigning.RefuseIncompleteOpen(commandKind, payload, reply);
        return new(requestId, reply);
    }

    private readonly record struct NativeDispatch(Guid RequestId, GrainOperationReply Reply);

    private static string Interrupted(OperationKind? commandKind) => commandKind switch
    {
        OperationKind.MovePartition => PartitionMovePublicProtocol.Interrupted,
        OperationKind.MaintainTextIndex => TextIndexMaintenanceProtocol.Interrupted,
        OperationKind.MaintainOnlineTextIndex => TextIndexMaintenanceProtocol.Interrupted,
        OperationKind.MaintainAnnIndex => AnnMaintenanceProtocol.Interrupted,
        _ => MultiLaneReceiveProtocol.Interrupted
    };

    /// <summary>Returns the actual operation identity, or null before actor dispatch began.</summary>
    /// <param name="context">The current HTTP request.</param>
    /// <returns>The request-local operation GUID if an execution started.</returns>
    internal static Guid? RequestId(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return OperationResponseHeaders.RequestId(context);
    }
}
