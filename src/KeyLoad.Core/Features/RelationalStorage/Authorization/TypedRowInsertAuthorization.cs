namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireTypedInsertPermissions(PrincipalRecord principal, PartitionRef partition,
        ResourceDefinition resource)
    {
        RequireDocumentAuthority(resource);
        if (resource.RelationalSchema is null)
        { throw Errors.Fail(ErrorCode.UnsupportedCapability, UntypedInsertMessage); }
        Authorization.Require(principal, partition, resource.Name, Capability.DocumentsWrite);
        Authorization.RequireWriteRow(principal, new RowAccess());
        foreach (var policy in resource.FieldPolicies)
        { Authorization.RequireFieldWrite(principal, resource, policy.Path); }
        foreach (var index in resource.Indexes)
        {
            foreach (var field in index.Fields)
            { Authorization.RequireFieldUse(principal, resource, field); }
        }
    }
    private const string UntypedInsertMessage = "An ordinary SQL INSERT requires a persisted typed table.";
}
