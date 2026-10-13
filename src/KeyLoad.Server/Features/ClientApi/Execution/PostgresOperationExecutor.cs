using KeyLoad.Orleans;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresOperationExecutor
{
    internal static async Task<PostgresEncodedReply> ExecuteAsync(IServiceProvider services, ServerConnectionFeature owner,
        PostgresSessionIdentity identity, PartitionRef partition, PostgresBoundPortal portal, int maximumRows, PostgresRetainedAdmission admission,
        CancellationToken token)
    {
        if (maximumRows < PostgresProtocol.AllRows)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
        if (portal.Completed)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
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
                QueryDialectVersion: portal.Statement.Rows ? profile.QueryDialectVersion : SqlOperationProtocol.DefaultQueryDialectVersion);
            var operation = SqlOperationCompiler.Compile(request, services.GetRequiredService<IOptions<DatabaseLimits>>(),
                options.MaximumCanonicalPayloadBytes, services.GetRequiredService<IOptions<QueryExecutionOptions>>(), token);
            if (operation.ReadKind != GrainReadKind.Query && operation.CommandKind != OperationKind.Batch)
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
            result.PossibleWrite = operation.CommandKind == OperationKind.Batch;
            var value = await CanonicalConnectionDispatch.ExecuteAsync(services, owner, principal, operation, token).ConfigureAwait(false);
            try
            {
                result.Payload = McpBoundedJson.Serialize(value, maximumJsonBytes);
                token.ThrowIfCancellationRequested();
            }
            catch (Exception failure) when (result.PossibleWrite && global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(failure))
            { throw Errors.Fail(ErrorCode.UnknownWriteOutcome, PostgresProtocol.InterruptedReply, failure); }
            portal.Completed = true;
        }, failures).ConfigureAwait(false);
        if (failures.Count != PostgresProtocol.AllRows)
        {
            if (result is not null)
            { ServerFailureObserver.Observe(result.Dispose, failures); }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        return result ?? throw Errors.Fail(ErrorCode.Corruption, PostgresProtocol.Invalid);
    }
}
