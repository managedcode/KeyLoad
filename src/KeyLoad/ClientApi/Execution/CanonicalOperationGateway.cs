using KeyLoad.Clustering;
using KeyLoad.Diagnostics.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

/// <summary>Shares authenticated request-actor dispatch across public protocol adapters.</summary>
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
        => readKind == GrainReadKind.FileMetadata || commandKind == OperationKind.FileMetadata
            ? throw Errors.Fail(ErrorCode.UnsupportedCapability, global::KeyLoad.Core.BlobStorage.FileStorageProtocol.BoundaryOnly)
            : ExecuteCoreAsync(context, readKind, commandKind, commandId, payload, cancellationToken);

    internal static async Task<object?> ExecuteFileMetadataAsync(HttpContext context,
        global::KeyLoad.Core.BlobStorage.FileMetadataQuery? query,
        global::KeyLoad.Core.BlobStorage.FileMetadataMutation? mutation, CancellationToken token)
    {
        if ((query is null) == (mutation is null))
        {
            throw Errors.Fail(ErrorCode.Validation, global::KeyLoad.Core.BlobStorage.FileStorageProtocol.Invalid);
        }
        var actual = await RuntimeExecuteAsync(context, query is null ? null : GrainReadKind.FileMetadata,
            mutation is null ? null : OperationKind.FileMetadata, mutation?.CommandId ?? Guid.Empty,
            query is null ? OrleansSerialization.Serialize(mutation!) : OrleansSerialization.Serialize(query), token);
        return OrleansSerialization.Deserialize<GrainValue>(actual.Reply.Payload.Span).Value;
    }

    private static async Task<DispatchedOperationReply> ExecuteCoreAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var execution = await RuntimeExecuteAsync(context, readKind, commandKind, commandId, payload,
            cancellationToken);
        var requestId = execution.RequestId;
        var reply = execution.Reply;
        try
        {
            var value = OrleansSerialization.Deserialize<GrainValue>(reply.Payload.Span);
            cancellationToken.ThrowIfCancellationRequested();
            return new(requestId, McpBoundedJson.Serialize((PublicReadResponse.Create(readKind, commandKind, value.Value)).Value,
                context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>().Value.MaximumDataReplyBytes));
        }
        catch (Exception failure) when (
            commandKind is OperationKind.ReceiveAcrossLanes or OperationKind.MaintainAnnIndex
                or OperationKind.MaintainTextIndex or OperationKind.MaintainOnlineTextIndex
                && failure is KeyLoadException or System.Text.Json.JsonException or ArgumentException)
        {
            GrainFailureDiagnostics.Log(context.RequestServices.GetRequiredService<ILogger<OrleansNode>>(),
                failure, requestId, GrainFailureStage.ReplyEncoding, ErrorCode.UnknownWriteOutcome);
            throw Errors.Fail(ErrorCode.UnknownWriteOutcome, Interrupted(commandKind));
        }
    }

    private static async Task<RuntimeDispatch> RuntimeExecuteAsync(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken)
    {
        var actual = CanonicalOperationRequestPreparation.Create(context, readKind, commandKind,
            commandId, payload, cancellationToken);
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
            reply = await node.ExecuteAsync(requestId, actual.Envelope, actual.Command, connection.Token);
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
        return new(requestId, reply);
    }

    private readonly record struct RuntimeDispatch(Guid RequestId, GrainOperationReply Reply);

    private static string Interrupted(OperationKind? commandKind) => commandKind switch
    {
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
