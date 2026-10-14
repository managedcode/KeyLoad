namespace KeyLoad.Server;

internal static class SqlOriginalMutationIdentity
{
    internal static void ValidateDeclared(Guid? declared)
    {
        if (declared == Guid.Empty)
        { throw ScriptDomOperationErrors.Invalid(); }
    }

    internal static Guid Required(Guid? declared)
    {
        if (declared is not { } identity || identity == Guid.Empty)
        { throw ScriptDomOperationErrors.Invalid(); }
        return identity;
    }

    internal static McpDecodedOperation Require(Guid? declared, McpDecodedOperation operation)
    {
        if (operation.CommandKind is null)
        {
            if (declared.HasValue)
            { throw ScriptDomOperationErrors.Invalid(); }
        }
        else if (operation.CommandId == Guid.Empty
            || declared is { } identity && identity != operation.CommandId)
        { throw ScriptDomOperationErrors.Invalid(); }
        return operation;
    }
}
