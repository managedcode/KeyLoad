namespace KeyLoad.Server;

internal static class PostgresStartupDecoder
{
    internal sealed record Startup(string Subject, PartitionRef Partition);

    internal static Startup Read(ReadOnlySpan<byte> payload, PostgresSessionOptions options)
    {
        var reader = new PostgresPayloadReader(payload);
        if (reader.Int32() != PostgresProtocol.Version)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
        string? user = null;
        string? database = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        while (true)
        {
            var key = reader.Text(options.MaximumStartupBytes);
            if (key.Length == PostgresProtocol.AllRows)
            { break; }
            if (!seen.Add(key))
            { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Invalid); }
            var value = reader.Text(options.MaximumStartupBytes);
            switch (key)
            {
                case User:
                    user = value;
                    break;
                case Database:
                    database = value;
                    break;
                case Encoding:
                    if (!StringComparer.Ordinal.Equals(value, Utf8))
                    { throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported); }
                    break;
                case Application:
                    break;
                default:
                    throw Errors.Fail(ErrorCode.UnsupportedCapability, PostgresProtocol.Unsupported);
            }
        }
        reader.Complete();
        if (string.IsNullOrEmpty(user) || database is null || !options.Databases.TryGetValue(database, out var partition))
        { throw Errors.Fail(ErrorCode.Unauthenticated, ServerProtocol.MissingCredential); }
        return new(user, partition);
    }

    internal const string User = "user";
    internal const string Database = "database";
    internal const string Encoding = "client_encoding";
    internal const string Application = "application_name";
    internal const string Utf8 = "UTF8";
}
