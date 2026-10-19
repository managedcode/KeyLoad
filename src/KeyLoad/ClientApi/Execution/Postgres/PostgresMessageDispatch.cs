using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class PostgresMessageDispatch(IServiceProvider services, global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured,
    PostgresSessionCatalog catalog, PostgresWireWriter writer, PostgresRetainedAdmission admission)
{
    private PostgresSessionOptions Options => configured.Value;
    internal void Prepare(PostgresFrame frame, CancellationToken token)
    {
        switch (frame.Type)
        {
            case PostgresMessageProtocol.Parse:
                var parsed = PostgresPreparedDecoder.Parse(frame.Payload, Options,
                    services.GetRequiredService<IOptions<DatabaseLimits>>(), token);
                catalog.PutStatement(parsed.Name, parsed.Statement);
                writer.Empty(PostgresWireWriter.ParseComplete);
                break;
            case PostgresMessageProtocol.Bind:
                var bound = PostgresPreparedDecoder.Bind(frame.Payload, catalog, Options,
                    services.GetRequiredService<IOptions<McpExecutionOptions>>(), token);
                catalog.PutPortal(bound.Name, bound.Portal);
                writer.Empty(PostgresWireWriter.BindComplete);
                break;
            case PostgresMessageProtocol.Describe:
                Describe(frame.Payload);
                break;
            case PostgresMessageProtocol.Close:
                Close(frame.Payload);
                break;
            default:
                throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
        }
    }

    internal async Task ExecuteAsync(PostgresFrame frame, ServerConnectionFeature owner,
        PostgresSessionIdentity identity, PartitionRef partition, PostgresCancellationSession cancellation, CancellationToken closed)
    {
        var (name, rows) = Execution(frame.Payload);
        var portal = catalog.Portal(name);
        await PostgresOperationDelivery.ExecuteAsync(services, writer, owner, identity, partition,
            portal, rows, cancellation, Options, admission, catalog, describe: false, closed);
    }

    private (string Name, int Rows) Execution(ReadOnlySpan<byte> payload)
    {
        var reader = new PostgresPayloadReader(payload);
        var name = reader.Text(Options.MaximumRetainedBytes);
        var rows = reader.Int32();
        reader.Complete();
        return (name, rows);
    }

    private void Describe(ReadOnlySpan<byte> payload)
    {
        var (kind, name) = Target(payload);
        if (kind == PostgresMessageProtocol.Statement)
        {
            PostgresSessionReplies.Parameters(writer, catalog.Statement(name));
        }
        else if (kind != PostgresMessageProtocol.Portal)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        var format = kind == PostgresMessageProtocol.Portal
            ? catalog.Portal(name).ResultFormat : PostgresProtocol.TextFormat;
        writer.Describe(PostgresSessionReplies.ResultColumn, format);
    }

    private void Close(ReadOnlySpan<byte> payload)
    {
        var (kind, name) = Target(payload);
        if (kind == PostgresMessageProtocol.Statement)
        {
            catalog.CloseStatement(name);
        }
        else if (kind == PostgresMessageProtocol.Portal)
        {
            catalog.ClosePortal(name);
        }
        else
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid);
        }
        writer.Empty(PostgresWireWriter.CloseComplete);
    }

    private (byte Kind, string Name) Target(ReadOnlySpan<byte> payload)
    {
        var reader = new PostgresPayloadReader(payload);
        var kind = reader.Byte();
        var name = reader.Text(Options.MaximumRetainedBytes);
        reader.Complete();
        return (kind, name);
    }
}
