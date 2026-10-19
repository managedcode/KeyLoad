using System.Text.Json;
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
                Options.MaximumFrameBytes, admission, context.ConnectionClosed);
            if (frame is null)
            {
                return;
            }
            if (frame.Type == PostgresMessageProtocol.Terminate)
            {
                Empty(frame.Payload);
                return;
            }
            if (draining && frame.Type != PostgresMessageProtocol.Sync)
            {
                continue;
            }
            try
            {
                switch (frame.Type)
                {
                    case PostgresMessageProtocol.Sync:
                        Sync(frame.Payload, draining);
                        draining = false;
                        writer.Ready();
                        await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed);
                        break;
                    case PostgresMessageProtocol.Flush:
                        Empty(frame.Payload);
                        await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed);
                        break;
                    case PostgresMessageProtocol.QueryMessage:
                        catalog.BeginQueryMessage();
                        await ExecuteQueryMessageAsync(frame.Payload, owner, identity, partition, cancellation, context.ConnectionClosed);
                        writer.Ready();
                        await PostgresControlFlush.RunAsync(services, writer, context.ConnectionClosed);
                        break;
                    case PostgresMessageProtocol.Execute:
                        await dispatch.ExecuteAsync(frame, owner, identity, partition, cancellation,
                            context.ConnectionClosed);
                        break;
                    default:
                        dispatch.Prepare(frame, context.ConnectionClosed);
                        break;
                }
            }
            catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error) && !context.ConnectionClosed.IsCancellationRequested)
            {
                var queryMessage = frame.Type == PostgresMessageProtocol.QueryMessage;
                draining = !queryMessage;
                await FailureAsync(error, queryMessage, context.ConnectionClosed);
            }
        }
    }

    private void Sync(byte[] payload, bool errorRecovery)
    {
        Empty(payload);
        catalog.Sync();
        if (errorRecovery)
        {
            catalog.ClearMutationIdentity();
        }
    }

    private async Task FailureAsync(Exception original, bool queryMessage, CancellationToken closed)
    {
        var failures = new List<Exception>();
        ServerFailureObserver.Observe(catalog.ClearMutationIdentity, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            PostgresErrorReply.Write(writer, original);
            if (queryMessage)
            {
                writer.Ready();
            }
            await PostgresControlFlush.RunAsync(services, writer, closed);
        }, failures);
        if (failures.Count != PostgresProtocol.AllRows)
        {
            failures.Insert(PostgresProtocol.FirstField, original);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private async Task ExecuteQueryMessageAsync(byte[] payload, ServerConnectionFeature owner, PostgresSessionIdentity identity,
        PartitionRef partition, PostgresCancellationSession cancellation, CancellationToken closed)
    {
        var sql = ReadQueryText(payload);
        if (string.IsNullOrWhiteSpace(sql))
        {
            writer.Empty(PostgresWireWriter.EmptyQueryResponse);
            return;
        }
        var inspection = PostgresPreparedDecoder.Inspect(sql, PostgresProtocol.AllRows,
            services.GetRequiredService<IOptions<DatabaseLimits>>(), closed);
        var statement = new PostgresPreparedStatement(sql, [], inspection.Rows,
            checked(sql.Length * sizeof(char)), inspection.UsesProfileDialect);
        var portal = new PostgresBoundPortal(statement, new Dictionary<string, JsonElement>(StringComparer.Ordinal),
            PostgresProtocol.TextFormat, statement.RetainedBytes);
        await PostgresOperationDelivery.ExecuteAsync(services, writer, owner, identity, partition,
            portal, PostgresProtocol.AllRows, cancellation, Options, admission, catalog, describe: true, closed);
    }

    private string ReadQueryText(ReadOnlySpan<byte> bytes)
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
