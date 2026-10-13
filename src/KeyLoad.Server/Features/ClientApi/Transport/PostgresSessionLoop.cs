using System.Text.Json;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresSessionLoop(IServiceProvider services, global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured,
    PostgresSessionCatalog catalog, PostgresWireWriter writer, PostgresRetainedAdmission admission)
{
    private PostgresSessionOptions Options => configured.Value;
    internal async Task RunAsync(Microsoft.AspNetCore.Connections.ConnectionContext context,
        ServerConnectionFeature owner, PostgresSessionIdentity identity, PartitionRef partition,
        PostgresCancellationSession cancellation)
    {
        var dispatch = new PostgresMessageDispatch(services, configured, catalog, writer, admission);
        var draining = false;
        while (true)
        {
            using var frame = await PostgresFrameReader.ReadAsync(context.Transport.Input, false,
                Options.MaximumFrameBytes, admission, context.ConnectionClosed).ConfigureAwait(false);
            if (frame is null)
            { return; }
            if (frame.Type == PostgresMessageProtocol.Terminate)
            { Empty(frame.Payload); return; }
            if (draining && frame.Type != PostgresMessageProtocol.Sync)
            { continue; }
            try
            {
                switch (frame.Type)
                {
                    case PostgresMessageProtocol.Sync:
                        Empty(frame.Payload);
                        catalog.Sync();
                        draining = false;
                        writer.Ready();
                        await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed).ConfigureAwait(false);
                        break;
                    case PostgresMessageProtocol.Flush:
                        Empty(frame.Payload);
                        await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed).ConfigureAwait(false);
                        break;
                    case PostgresMessageProtocol.SimpleQuery:
                        catalog.Sync();
                        await SimpleAsync(frame.Payload, owner, identity, partition, cancellation, context.ConnectionClosed).ConfigureAwait(false);
                        writer.Ready();
                        await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed).ConfigureAwait(false);
                        break;
                    case PostgresMessageProtocol.Execute:
                        await dispatch.ExecuteAsync(frame, owner, identity, partition, cancellation,
                            context.ConnectionClosed).ConfigureAwait(false);
                        break;
                    default:
                        dispatch.Prepare(frame, context.ConnectionClosed);
                        break;
                }
            }
            catch (Exception error) when (global::KeyLoad.Orleans.NativeCqrsBoundaryErrors.IsNonFatal(error) && !context.ConnectionClosed.IsCancellationRequested)
            {
                PostgresErrorReply.Write(writer, error);
                if (frame.Type == PostgresMessageProtocol.SimpleQuery)
                { writer.Ready(); }
                else
                { draining = true; }
                await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed).ConfigureAwait(false);
            }
        }
    }

    private async Task SimpleAsync(byte[] payload, ServerConnectionFeature owner, PostgresSessionIdentity identity,
        PartitionRef partition, PostgresCancellationSession cancellation, CancellationToken closed)
    {
        var sql = SimpleSql(payload);
        if (string.IsNullOrWhiteSpace(sql))
        { writer.Empty(PostgresWireWriter.EmptyQueryResponse); return; }
        var statement = new PostgresPreparedStatement(PostgresParameterNames.Translate(sql,
            PostgresProtocol.AllRows, services.GetRequiredService<IOptions<DatabaseLimits>>().Value.MaxQueryDepth,
            services.GetRequiredService<IOptions<QueryExecutionOptions>>().Value.SqlBudgetCheckInterval, closed), [], IsQuery(sql, closed),
            checked(sql.Length * sizeof(char)));
        var portal = new PostgresBoundPortal(statement, new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            PostgresProtocol.TextFormat, statement.RetainedBytes);
        await PostgresOperationDelivery.ExecuteAsync(services, writer, owner, identity, partition,
            portal, PostgresProtocol.AllRows, cancellation, Options, admission, describe: true, closed).ConfigureAwait(false);
    }

    private bool IsQuery(string sql, CancellationToken token)
    {
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>().Value;
        var reader = new SqlOperationSyntaxReader(sql, limits.MaxQueryTokens, limits.MaxQueryDepth,
            services.GetRequiredService<IOptions<QueryExecutionOptions>>(), token);
        var kind = reader.Identifier();
        return kind.Equals(SqlOperationSyntax.Select, StringComparison.OrdinalIgnoreCase)
            || kind.Equals(SqlOperationSyntax.Explain, StringComparison.OrdinalIgnoreCase);
    }

    private string SimpleSql(ReadOnlySpan<byte> bytes)
    {
        var reader = new PostgresPayloadReader(bytes);
        var sql = reader.Text(services.GetRequiredService<IOptions<DatabaseLimits>>().Value.MaxQueryBytes);
        reader.Complete();
        return sql;
    }

    private static void Empty(ReadOnlySpan<byte> payload)
    {
        var reader = new PostgresPayloadReader(payload);
        reader.Complete();
    }
}
