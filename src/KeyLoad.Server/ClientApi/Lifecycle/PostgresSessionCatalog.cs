namespace KeyLoad.Server;

internal sealed class PostgresSessionCatalog(global::Microsoft.Extensions.Options.IOptions<PostgresSessionOptions> configured, PostgresRetainedAdmission admission) : IDisposable
{
    private PostgresSessionOptions Options => configured.Value;
    private const string Capacity = "The native SQL statement or portal grant is exhausted.";
    private const string Missing = "The native SQL statement or portal is unavailable.";
    private readonly Dictionary<string, PostgresPreparedStatement> statements = new(configured.Value.MaximumStatements, StringComparer.Ordinal);
    private readonly Dictionary<string, PostgresBoundPortal> portals = new(StringComparer.Ordinal);
    private int retained;
    private Guid? mutationIdentity;
    private const int MutationIdentityBytes = 16;
    internal PostgresPreparedStatement Statement(string name)
        => statements.TryGetValue(name, out var value) ? value : throw Errors.Fail(ErrorCode.Validation, Missing);
    internal PostgresBoundPortal Portal(string name)
        => portals.TryGetValue(name, out var value) ? value : throw Errors.Fail(ErrorCode.Validation, Missing);
    internal void PutStatement(string name, PostgresPreparedStatement value)
    {
        statements.TryGetValue(name, out var previous);
        if (previous is not null && name.Length != PostgresProtocol.AllRows)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Duplicate);
        }
        if (previous is null && statements.Count >= Options.MaximumStatements)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity);
        }
        ReplaceStatement(name, previous, value);
    }
    internal void PutPortal(string name, PostgresBoundPortal value)
    {
        portals.TryGetValue(name, out var previous);
        if (previous is not null && name.Length != PostgresProtocol.AllRows)
        {
            throw Errors.Fail(ErrorCode.Validation, PostgresProtocol.Duplicate);
        }
        if (previous is null && portals.Count >= Options.MaximumPortals)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity);
        }
        Resize(previous?.RetainedBytes ?? PostgresProtocol.EmptyBytes, value.RetainedBytes);
        portals[name] = value;
    }
    internal void CloseStatement(string name)
    {
        if (statements.TryGetValue(name, out var value))
        {
            ReplaceStatement(name, value, null);
        }
    }

    private void ReplaceStatement(string name, PostgresPreparedStatement? previous, PostgresPreparedStatement? next)
    {
        var dependent = portals.Where(pair => ReferenceEquals(pair.Value.Statement, previous))
            .Select(pair => pair.Key).ToArray();
        var released = previous?.RetainedBytes ?? PostgresProtocol.EmptyBytes;
        foreach (var key in dependent)
        {
            released = checked(released + portals[key].RetainedBytes);
        }
        // Admit the complete replacement before changing any catalog ownership.
        Resize(released, next?.RetainedBytes ?? PostgresProtocol.EmptyBytes);
        foreach (var key in dependent)
        {
            portals.Remove(key);
        }
        if (next is null)
        {
            statements.Remove(name);
        }
        else
        {
            statements[name] = next;
        }
    }
    internal void BeginQueryMessage()
    {
        Sync();
        CloseStatement(string.Empty);
    }
    internal void ClosePortal(string name)
    {
        if (portals.Remove(name, out var value))
        {
            Resize(value.RetainedBytes, PostgresProtocol.EmptyBytes);
        }
    }

    internal void Sync()
    {
        foreach (var value in portals.Values)
        {
            Resize(value.RetainedBytes, PostgresProtocol.EmptyBytes);
        }
        portals.Clear();
    }
    internal void ArmMutationIdentity(Guid identity)
    {
        if (mutationIdentity.HasValue)
        {
            ClearMutationIdentity();
            throw ScriptDomOperationErrors.Invalid();
        }
        if (identity == Guid.Empty)
        {
            throw ScriptDomOperationErrors.Invalid();
        }
        Resize(PostgresProtocol.EmptyBytes, MutationIdentityBytes);
        mutationIdentity = identity;
    }
    internal Guid? TakeMutationIdentity()
    {
        var value = mutationIdentity;
        ClearMutationIdentity();
        return value;
    }
    internal void ClearMutationIdentity()
    {
        if (mutationIdentity.HasValue)
        {
            Resize(MutationIdentityBytes, PostgresProtocol.EmptyBytes);
            mutationIdentity = null;
        }
    }

    private void Resize(int previous, int next)
    {
        var replacement = checked(retained - previous + next);
        if (replacement > Options.MaximumRetainedBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, Capacity);
        }
        admission.Resize(retained, replacement);
        retained = replacement;
    }
    public void Dispose()
    {
        admission.Resize(retained, PostgresProtocol.EmptyBytes);
        retained = PostgresProtocol.EmptyBytes;
        mutationIdentity = null;
        statements.Clear();
        portals.Clear();
    }
}

