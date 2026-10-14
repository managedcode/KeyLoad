using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresPreparedDecoder
{
    internal static (string Name, PostgresPreparedStatement Statement) Parse(ReadOnlySpan<byte> payload,
        PostgresSessionOptions options, IOptions<DatabaseLimits> limitsOptions, CancellationToken token)
    {
        var reader = new PostgresPayloadReader(payload);
        var name = reader.Text(options.MaximumRetainedBytes);
        var sql = reader.Text(Math.Min(options.MaximumRetainedBytes, limitsOptions.Value.MaxQueryBytes));
        var count = reader.Int16();
        if (count < PostgresProtocol.AllRows || count > PostgresProtocol.MaximumParameters)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PostgresProtocol.Invalid); }
        var oids = new int[count];
        for (var index = PostgresProtocol.AllRows; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            oids[index] = reader.Int32();
            RequireOid(oids[index]);
        }
        reader.Complete();
        var inspection = Inspect(sql, count, limitsOptions, token);
        var bytes = checked((name.Length + sql.Length) * sizeof(char) + oids.Length * sizeof(int));
        return (name, new(sql, oids, inspection.Rows, bytes, inspection.UsesProfileDialect));
    }

    internal static (bool Rows, bool UsesProfileDialect) Inspect(string sql, int positionalParameterCount,
        IOptions<DatabaseLimits> limitsOptions, CancellationToken token)
    {
        var syntax = global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.Parse(sql, limitsOptions,
            token.ThrowIfCancellationRequested, token);
        global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.ValidateNativeSessionSlots(syntax,
            positionalParameterCount, token.ThrowIfCancellationRequested, token);
        var rows = global::KeyLoad.Features.QueryExecution.ScriptDomSyntax.HasQueryRows(syntax);
        if (!rows && syntax is not (global::Microsoft.SqlServer.TransactSql.ScriptDom.ExecuteStatement
            or global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
        var joinedInsert = syntax is global::Microsoft.SqlServer.TransactSql.ScriptDom.InsertStatement
        {
            InsertSpecification.InsertSource: global::Microsoft.SqlServer.TransactSql.ScriptDom.SelectInsertSource
            {
                Select: global::Microsoft.SqlServer.TransactSql.ScriptDom.QuerySpecification
                { FromClause.TableReferences: [global::Microsoft.SqlServer.TransactSql.ScriptDom.QualifiedJoin] }
            }
        };
        return (rows, joinedInsert);
    }

    internal static (string Name, PostgresBoundPortal Portal) Bind(ReadOnlySpan<byte> payload,
        PostgresSessionCatalog catalog, PostgresSessionOptions options, IOptions<McpExecutionOptions> framing, CancellationToken token)
    {
        var reader = new PostgresPayloadReader(payload);
        var name = reader.Text(options.MaximumRetainedBytes);
        var statement = catalog.Statement(reader.Text(options.MaximumRetainedBytes));
        var formats = Formats(ref reader, statement.ParameterOids.Length);
        var count = reader.Int16();
        if (count != statement.ParameterOids.Length)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
        var values = new Dictionary<string, JsonElement>(count, StringComparer.Ordinal);
        var retained = checked(name.Length * sizeof(char) + statement.Sql.Length * sizeof(char)
            + statement.ParameterOids.Length * sizeof(int));
        for (var index = PostgresProtocol.AllRows; index < count; index++)
        {
            token.ThrowIfCancellationRequested();
            var length = reader.Int32();
            if (length < PostgresProtocol.NullLength)
            { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
            if (length > options.MaximumRetainedBytes - retained)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, PostgresProtocol.Invalid); }
            var key = PostgresProtocol.ParameterPrefix + (index + sizeof(byte)).ToString(CultureInfo.InvariantCulture);
            var format = formats.Length == PostgresProtocol.AllRows ? PostgresProtocol.TextFormat
                : formats.Length == sizeof(byte) ? formats[PostgresProtocol.AllRows] : formats[index];
            var value = length == PostgresProtocol.NullLength ? JsonSerializer.SerializeToElement<object?>(null, JsonDefaults.Options)
                : PostgresParameterDecoder.Read(statement.ParameterOids[index], format, reader.Take(length), framing);
            values.Add(key, value);
            retained = checked(retained + key.Length * sizeof(char) + value.GetRawText().Length * sizeof(char));
            if (retained > options.MaximumRetainedBytes)
            { throw Errors.Fail(ErrorCode.ResourceExhausted, PostgresProtocol.Invalid); }
        }
        var resultFormats = Formats(ref reader, sizeof(byte));
        reader.Complete();
        var resultFormat = resultFormats.Length == PostgresProtocol.AllRows ? PostgresProtocol.TextFormat : resultFormats[PostgresProtocol.AllRows];
        return (name, new(statement, values, resultFormat, retained));
    }

    private static short[] Formats(ref PostgresPayloadReader reader, int columns)
    {
        var count = reader.Int16();
        if (count != PostgresProtocol.AllRows && count != sizeof(byte) && count != columns)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
        var result = new short[count];
        for (var index = PostgresProtocol.AllRows; index < count; index++)
        {
            result[index] = reader.Int16();
            if (result[index] is not (PostgresProtocol.TextFormat or PostgresProtocol.BinaryFormat))
            { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
        }
        return result;
    }

    private static void RequireOid(int oid)
    {
        if (oid is not (PostgresProtocol.Int16Oid or PostgresProtocol.Int32Oid or PostgresProtocol.Int64Oid or PostgresProtocol.BooleanOid
            or PostgresProtocol.TextOid or PostgresProtocol.JsonOid or PostgresProtocol.JsonbOid or PostgresNumericProtocol.Oid))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
    }
}
