using System.Globalization;
using System.Text.Json;
using KeyLoad.Query;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal static class PostgresPreparedDecoder
{
    internal static (string Name, PostgresPreparedStatement Statement) Parse(ReadOnlySpan<byte> payload,
        PostgresSessionOptions options, DatabaseLimits limits, IOptions<QueryExecutionOptions> queryOptions, CancellationToken token)
    {
        var reader = new PostgresPayloadReader(payload);
        var name = reader.Text(options.MaximumRetainedBytes);
        var sql = reader.Text(Math.Min(options.MaximumRetainedBytes, limits.MaxQueryBytes));
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
        var translated = PostgresParameterNames.Translate(sql, count, limits.MaxQueryDepth, queryOptions.Value.SqlBudgetCheckInterval, token);
        var syntax = new SqlOperationSyntaxReader(translated, limits.MaxQueryTokens, limits.MaxQueryDepth, queryOptions, token);
        var kind = syntax.Identifier();
        var rows = kind.Equals(SqlOperationSyntax.Select, StringComparison.OrdinalIgnoreCase);
        if (!rows && !kind.Equals(SqlOperationSyntax.Explain, StringComparison.OrdinalIgnoreCase)
            && !kind.Equals(SqlOperationSyntax.Call, StringComparison.OrdinalIgnoreCase)
            && !kind.Equals(SqlCompositionInsert.Statement, StringComparison.OrdinalIgnoreCase))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
        var bytes = checked((name.Length + translated.Length) * sizeof(char) + oids.Length * sizeof(int));
        return (name, new(translated, oids, rows || kind.Equals(SqlOperationSyntax.Explain, StringComparison.OrdinalIgnoreCase), bytes));
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
        if (oid is not (PostgresProtocol.Int32Oid or PostgresProtocol.Int64Oid or PostgresProtocol.BooleanOid
            or PostgresProtocol.TextOid or PostgresProtocol.JsonOid or PostgresProtocol.JsonbOid))
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
    }
}
