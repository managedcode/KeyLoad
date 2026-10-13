namespace KeyLoad.Server;

internal sealed class PostgresSessionCatalog(global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured, PostgresRetainedAdmission admission) : IDisposable
{
    private PostgresSessionOptions Options => configured.Value;
    private const string Capacity = "The native SQL statement or portal grant is exhausted.";
    private const string Missing = "The native SQL statement or portal is unavailable.";
    private readonly Dictionary<string, PostgresPreparedStatement> statements = new(configured.Value.MaximumStatements, StringComparer.Ordinal);
    private readonly Dictionary<string, PostgresBoundPortal> portals = new(StringComparer.Ordinal);
    private int retained;

    internal PostgresPreparedStatement Statement(string name)
        => statements.TryGetValue(name, out var value) ? value : throw Errors.Fail(ErrorCode.Validation, Missing);

    internal PostgresBoundPortal Portal(string name)
        => portals.TryGetValue(name, out var value) ? value : throw Errors.Fail(ErrorCode.Validation, Missing);

    internal void PutStatement(string name, PostgresPreparedStatement value)
    {
        statements.TryGetValue(name, out var previous);
        if (previous is not null && name.Length != PostgresProtocol.AllRows)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Duplicate); }
        if (previous is null && statements.Count >= Options.MaximumStatements)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity); }
        Resize(previous?.RetainedBytes ?? PostgresProtocol.EmptyBytes, value.RetainedBytes);
        statements[name] = value;
        if (name.Length == PostgresProtocol.AllRows)
        { ClosePortal(string.Empty); }
    }

    internal void PutPortal(string name, PostgresBoundPortal value)
    {
        portals.TryGetValue(name, out var previous);
        if (previous is not null && name.Length != PostgresProtocol.AllRows)
        { throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Duplicate); }
        if (previous is null && portals.Count >= Options.MaximumPortals)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity); }
        Resize(previous?.RetainedBytes ?? PostgresProtocol.EmptyBytes, value.RetainedBytes);
        portals[name] = value;
    }

    internal void CloseStatement(string name)
    {
        if (statements.Remove(name, out var value))
        { Resize(value.RetainedBytes, PostgresProtocol.EmptyBytes); }
    }

    internal void ClosePortal(string name)
    {
        if (portals.Remove(name, out var value))
        { Resize(value.RetainedBytes, PostgresProtocol.EmptyBytes); }
    }

    internal void Sync()
    {
        foreach (var value in portals.Values)
        { Resize(value.RetainedBytes, PostgresProtocol.EmptyBytes); }
        portals.Clear();
    }

    private void Resize(int previous, int next)
    {
        var replacement = checked(retained - previous + next);
        if (replacement > Options.MaximumRetainedBytes)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity); }
        admission.Resize(retained, replacement);
        retained = replacement;
    }

    public void Dispose()
    {
        admission.Resize(retained, PostgresProtocol.EmptyBytes);
        retained = PostgresProtocol.EmptyBytes;
        statements.Clear();
        portals.Clear();
    }
}
