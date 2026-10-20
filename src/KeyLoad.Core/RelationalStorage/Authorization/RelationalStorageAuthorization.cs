namespace KeyLoad.Core;
/// <summary>Executes RelationalStorageAuthorization operations against the caller's database context.</summary>
public static class RelationalStorageAuthorization
{
    internal static void RequireTypedInsertPermissions(this DatabaseEngine engine, PrincipalRecord principal, PartitionRef partition, ResourceDefinition resource)
    {
        DocumentStorageCommands.RequireDocumentAuthority(resource);
        if (resource.RelationalSchema is null)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, UntypedInsertMessage);
        }

        engine.Authorization.Require(principal, partition, resource.Name, Capability.DocumentsWrite);
        engine.Authorization.RequireWriteRow(principal, new RowAccess());
        foreach (var policy in resource.FieldPolicies)
        {
            engine.Authorization.RequireFieldWrite(principal, resource, policy.Path);
        }

        foreach (var index in resource.Indexes)
        {
            foreach (var field in index.Fields)
            {
                engine.Authorization.RequireFieldUse(principal, resource, field);
            }
        }
    }

    internal const string UntypedInsertMessage = "An ordinary SQL INSERT requires a persisted typed table.";
}
