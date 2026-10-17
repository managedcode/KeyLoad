using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Indexes an authenticated public identity before any reusable-name resolution.</summary>
internal static class ResourceRetirementDropParentStorage
{
    private const string ParentSpace = "resource-retirement-drop-parent-v1";
    private const string IdentitySpace = "resource-retirement-drop-parent-id-v1";

    internal static byte[] Key(ResourceRetirementDropParentLookupV1 lookup, string principal)
        => KeyCodec.Encode(ParentSpace, lookup.Request.TenantId, lookup.Request.DatabaseId,
            lookup.Request.ResourceName, principal, lookup.CommandId);

    private static byte[] IdentityKey(string principal, Guid id) => KeyCodec.Encode(IdentitySpace, principal, id);

    internal static ResourceRetirementDropParentV1? Read(IKeyValueView view,
        ResourceRetirementDropParentLookupV1 lookup, string principal, DatabaseLimits limits)
    {
        RequireLookup(lookup, limits);
        var original = ResourceRetirementStorage.Read<ResourceRetirementDropParentLookupV1>(
            view, IdentityKey(principal, lookup.CommandId), limits);
        if (original is not null && !ResourceRetirementValidation.Same(original, lookup))
        {
            throw Errors.Fail(ErrorCode.Conflict, ResourceRetirementProtocol.Changed);
        }
        var parent = ResourceRetirementStorage.Read<ResourceRetirementDropParentV1>(view, Key(lookup, principal), limits);
        if ((parent is null) != (original is null))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        if (parent is not null && (parent.PrincipalId != principal
            || !ResourceRetirementValidation.Same(parent.Lookup, lookup)))
        {
            throw Errors.Fail(ErrorCode.Corruption, ResourceRetirementProtocol.Malformed);
        }
        return parent;
    }

    internal static void Write(IAtomicTransaction tx, ResourceRetirementDropParentV1 parent, DatabaseLimits limits)
    {
        ResourceRetirementValidation.Bound(parent, limits);
        tx.PutRecord(IdentityKey(parent.PrincipalId, parent.Lookup.CommandId), parent.Lookup);
        tx.PutRecord(Key(parent.Lookup, parent.PrincipalId), parent);
    }

    internal static void RequireLookup(ResourceRetirementDropParentLookupV1 lookup, DatabaseLimits limits)
    {
        if (lookup is null || lookup.Version != ResourceRetirementProtocol.Version
            || lookup.CommandId == Guid.Empty || lookup.Request is null)
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        JsonData.Identifier(lookup.Request.TenantId);
        JsonData.Identifier(lookup.Request.DatabaseId);
        JsonData.Identifier(lookup.Request.ResourceName);
        ResourceRetirementValidation.Bound(lookup, limits);
    }
}
