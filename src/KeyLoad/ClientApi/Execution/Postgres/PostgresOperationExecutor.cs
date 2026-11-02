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
            var principal = await identity.VerifyAsync(services, owner, token);
            var profile = services.GetRequiredService<IOptions<PostgresSessionOptions>>().Value;
            result = PostgresEncodedReply.Admit(profile, options,
                admission, out var maximumJsonBytes);
            var request = new SqlOperationRequest(partition, portal.Statement.Sql, portal.Parameters,
                AllowFullScan: portal.Statement.Rows && profile.AllowFullScan,
                QueryDialectVersion: portal.Statement.Rows || portal.Statement.UsesProfileDialect
                    ? profile.QueryDialectVersion : SqlOperationProtocol.DefaultQueryDialectVersion,
                CommandId: mutationIdentity);
            var compilation = SqlOperationCompiler.CompileSessionPlan(request,
                options.MaximumCanonicalPayloadBytes, services.GetRequiredService<IOptions<QueryExecutionOptions>>(),
                portal.Statement.ParameterOids.Length, token);
            if (TryCompleteControl(compilation, catalog, result, maximumJsonBytes, portal, token))
            {
                return;
            }
            var operation = compilation.Operation ?? throw ScriptDomOperationErrors.Unsupported();
            if (operation.ReadKind is not (GrainReadKind.Query or GrainReadKind.AstQuery
                or GrainReadKind.Document or GrainReadKind.Message or GrainReadKind.Traverse)
                && operation.CommandKind is not (OperationKind.Batch or OperationKind.CreateTable or OperationKind.DropTable))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
            }
            result.PossibleWrite = operation.CommandKind is OperationKind.Batch or OperationKind.CreateTable or OperationKind.DropTable;
            var /* KLD0041: Retain the terminal public DTO only between joined connection/context cleanup and the following bounded PostgreSQL JSON encoding. */ value = await (CanonicalConnectionDispatch.ExecuteAsync(services, owner, principal, operation, token) /* KLD0041: Complete DTO mapping and joined connection/context cleanup before bounded PostgreSQL JSON encoding; preserve possible-write failure classification. */);
            try
            {
                result.Payload = (McpBoundedJson.Serialize(value, maximumJsonBytes) /* KLD0041: Encode the final heterogeneous public DTO with the configured byte bound; an encoding failure after a possible write remains an unknown write outcome. */);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception failure) when (result.PossibleWrite && global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(failure))
            {
                throw Errors.Fail(ErrorCode.UnknownWriteOutcome, PostgresProtocol.InterruptedReply, failure);
            }
            portal.Completed = true;
        }, failures);
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

    private static bool TryCompleteControl(SqlSessionCompilation compilation, PostgresSessionCatalog catalog,
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
