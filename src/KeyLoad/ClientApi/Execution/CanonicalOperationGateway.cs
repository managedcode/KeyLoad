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
    internal static Task<DispatchedOperationReply> ExecuteAsync<TPayload>(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, TPayload payload, CancellationToken cancellationToken) where TPayload : notnull
        => readKind == GrainReadKind.FileMetadata || commandKind == OperationKind.FileMetadata
            ? throw Errors.Fail(ErrorCode.UnsupportedCapability, global::KeyLoad.Core.BlobStorage.FileStorageProtocol.BoundaryOnly)
            : ExecuteCoreAsync(context, readKind, commandKind, commandId, payload, cancellationToken);

    internal static async Task<TResult> ExecuteFileMetadataAsync<TResult>(HttpContext context,
        global::KeyLoad.Core.BlobStorage.FileMetadataQuery? query,
        global::KeyLoad.Core.BlobStorage.FileMetadataMutation? mutation, CancellationToken token)
    {
        if ((query is null) == (mutation is null))
        {
            throw Errors.Fail(ErrorCode.Validation, global::KeyLoad.Core.BlobStorage.FileStorageProtocol.Invalid);
        }
        var actual = query is not null
            ? await RuntimeExecuteAsync(context, GrainReadKind.FileMetadata, null, Guid.Empty, query, token)
            : await RuntimeExecuteAsync(context, null, OperationKind.FileMetadata, mutation!.CommandId, mutation!, token);
        var /* KLD0041: Read the current generated file-metadata reply slot once and immediately require the caller's concrete TResult; no object task is introduced. */ value = (GrainReplyFactory.RequireValue(actual.Reply).Value /* KLD0041: Read the current generated file-metadata reply slot once and immediately require the caller's concrete TResult; no object task is introduced. */);
        return (value is TResult result /* KLD0042: Require the exact file-metadata TResult from the native GrainValue wire slot; the public method returns Task<TResult>. */) ? result
            : throw Errors.Fail(ErrorCode.Corruption, global::KeyLoad.Core.BlobStorage.FileStorageProtocol.Invalid);
    }

    private static async Task<DispatchedOperationReply> ExecuteCoreAsync<TPayload>(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, TPayload payload, CancellationToken cancellationToken) where TPayload : notnull
    {
        var execution = await RuntimeExecuteAsync(context, readKind, commandKind, commandId, payload,
            cancellationToken);
        var requestId = execution.RequestId;
        var reply = execution.Reply;
        try
        {
            var value = GrainReplyFactory.RequireValue(reply);
            cancellationToken.ThrowIfCancellationRequested();
            return new(requestId, (McpBoundedJson.Serialize((((PublicReadResponse.Create(readKind, commandKind, (value.Value /* KLD0041: Read the existing generated GrainValue slot at the terminal public JSON projection boundary without changing its binary layout. */)) /* KLD0041: Project the runtime wire reply to its concrete public DTO only at the bounded HTTP JSON response boundary. */)).Value /* KLD0041: Read the final public DTO from the heterogeneous response projection immediately before bounded JSON encoding. */),
                context.RequestServices.GetRequiredService<IOptions<McpExecutionOptions>>().Value.MaximumDataReplyBytes) /* KLD0041: Encode the terminal public DTO using its runtime type and the configured reply-byte bound; preserve unknown-write-outcome handling for encoding failures. */));
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

    private static async Task<RuntimeDispatch> RuntimeExecuteAsync<TPayload>(HttpContext context, GrainReadKind? readKind,
        OperationKind? commandKind, Guid commandId, TPayload payload, CancellationToken cancellationToken) where TPayload : notnull
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
            try
            {
                reply = await node.ExecuteAsync(requestId, actual.Envelope, actual.Command, connection.Token);
            }
            finally
            {
                if (commandKind == OperationKind.ConfigureApiKey && payload is ConfigureApiKeyRequest changed)
                {
                    // A changed credential is read from storage again on its next request.
                    await node.Grains!.GetGrain<IUserGrain>(UserGrainIdentity.For(changed.ApiKey.Id)).InvalidateAsync();
                }
            }
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
        ArgumentNullException.ThrowIfNull(context) /* KLD0041: BCL null validation accepts the context HttpContext reference through its object parameter. */;
        return OperationResponseHeaders.RequestId(context);
    }
}
