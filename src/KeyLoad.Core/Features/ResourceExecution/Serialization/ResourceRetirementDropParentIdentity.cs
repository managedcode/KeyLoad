using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ResourceExecution;

/// <summary>Names only public-header persistence/completion; it does not infer a current D1 or execution owner.</summary>
internal static class ResourceRetirementDropParentIdentity
{
    private const string Domain = "keyload.resource-retirement.drop-parent-id.v1";
    private const int GuidByteCount = 16;

    internal static Guid CommandId(ResourceRetirementDropParentLookupV1 lookup, ResourceRetirementPhase phase)
    {
        if (lookup is null || lookup.Version != ResourceRetirementProtocol.Version
            || lookup.CommandId == Guid.Empty || lookup.Request is null
            || phase is not (ResourceRetirementPhase.PersistDropParent or ResourceRetirementPhase.CompleteDropParent))
        {
            throw Errors.Fail(ErrorCode.Validation, ResourceRetirementProtocol.Invalid);
        }
        JsonData.Identifier(lookup.Request.TenantId);
        JsonData.Identifier(lookup.Request.DatabaseId);
        JsonData.Identifier(lookup.Request.ResourceName);
        var identity = KeyCodec.Encode(Domain, lookup.Version, lookup.CommandId,
            lookup.Request.TenantId, lookup.Request.DatabaseId, lookup.Request.ResourceName, (int)phase);
        return new Guid(SHA256.HashData(identity).AsSpan()[..GuidByteCount]);
    }
}
