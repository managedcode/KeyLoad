using KeyLoad.Clustering;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresOperationExecutor
{
    internal static async Task<PostgresEncodedReply> ExecuteAsync(IServiceProvider services, ServerConnectionFeature owner,
        PostgresSessionIdentity identity, PartitionRef partition, PostgresBoundPortal portal, int maximumRows, PostgresRetainedAdmission admission,
        PostgresSessionCatalog catalog, CancellationToken token)
    {
        var mutationIdentity = catalog.TakeMutationIdentity();
        if (maximumRows < PostgresProtocol.AllRows)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        if (portal.Completed)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
        }
        PostgresEncodedReply? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var options = services.GetRequiredService<IOptions<McpExecutionOptions>>().Value;
            var principal = await identity.VerifyAsync(services, owner, token).ConfigureAwait(false);
            var profile = services.GetRequiredService<IOptions<PostgresSessionOptions>>().Value;
            result = PostgresEncodedReply.Admit(profile, options,
                admission, out var maximumJsonBytes);
            var request = new SqlOperationRequest(partition, portal.Statement.Sql, portal.Parameters,
                AllowFullScan: portal.Statement.Rows && profile.AllowFullScan,
                QueryDialectVersion: portal.Statement.Rows || portal.Statement.UsesProfileDialect
                    ? profile.QueryDialectVersion : SqlOperationProtocol.DefaultQueryDialectVersion,
                CommandId: mutationIdentity);
            var compilation = SqlOperationCompiler.CompileNativeSessionPlan(request, services.GetRequiredService<IOptions<DatabaseLimits>>(),
                options.MaximumCanonicalPayloadBytes, services.GetRequiredService<IOptions<QueryExecutionOptions>>(),
                portal.Statement.ParameterOids.Length, token);
            if (TryCompleteControl(compilation, catalog, result, maximumJsonBytes, portal, token))
            {
                return;
            }
            var operation = compilation.Operation ?? throw ScriptDomOperationErrors.Unsupported();
            if (operation.ReadKind is not (GrainReadKind.Query or GrainReadKind.AstQuery
                or GrainReadKind.Document or GrainReadKind.Message or GrainReadKind.Traverse)
                && operation.CommandKind is not (OperationKind.Batch or OperationKind.CreateTable))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
            }
            result.PossibleWrite = operation.CommandKind is OperationKind.Batch or OperationKind.CreateTable;
            var value = await CanonicalConnectionDispatch.ExecuteAsync(services, owner, principal, operation, token).ConfigureAwait(false);
            try
            {
                result.Payload = McpBoundedJson.Serialize(value, maximumJsonBytes);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception failure) when (result.PossibleWrite && global::KeyLoad.Clustering.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, PostgresProtocol.InterruptedReply, failure);
            }
            portal.Completed = true;
        }, failures).ConfigureAwait(false);
        CompleteFailure(catalog, result, failures);
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PostgresProtocol.Invalid);
    }
    private static void CompleteFailure(PostgresSessionCatalog catalog, PostgresEncodedReply? result, List<Exception> failures)
    {
        if (failures.Count != PostgresProtocol.AllRows)
        {
            ServerFailureObserver.Observe(catalog.ClearMutationIdentity, failures);
            if (result is not null)
            {
                ServerFailureObserver.Observe(result.Dispose, failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static bool TryCompleteControl(SqlNativeSessionCompilation compilation, PostgresSessionCatalog catalog,
        PostgresEncodedReply reply, int maximumJsonBytes, PostgresBoundPortal portal, CancellationToken token)
    {
        if (compilation.ContextCommandId is not { } contextId)
        {
            return false;
        }
        catalog.ArmMutationIdentity(contextId);
        reply.Payload = McpBoundedJson.Serialize(true, maximumJsonBytes);
        token.ThrowIfCancellationRequested();
        portal.Completed = true;
        return true;
    }
}
