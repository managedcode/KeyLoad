using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorParentInventoryAuthorization
{
    internal static void Require(IAuthorizationPolicy authorization, PrincipalRecord principal, EventVectorOwnedInventory inventory,
        EventVectorAdmissionPolicy admission, EventVectorReadLifetimeBinding? lifetimeBinding = null)
    {
        if (inventory.Header is not { } map)
        {
            return;
        }
        var parentKey = EventVectorKeys.ParentCall(map.ControlPartition, map.MapId);
        var cleanupKey = EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId);
        foreach (var row in inventory.PayloadRows)
        {
            if (row.Key.Span.SequenceEqual(parentKey))
            {
                var parent = EventVectorParentValidation.Read(map, row, admission);
                AuthorizeScopes(authorization, principal, parent.OriginalAuthorizedSourceScopes, admission, lifetimeBinding);
            }
            else if (row.Key.Span.SequenceEqual(cleanupKey))
            {
                var cleanup = EventVectorCleanupValidation.Read(map, row, admission);
                AuthorizeScopes(authorization, principal, cleanup.OriginalAuthorizedSourceScopes, admission, lifetimeBinding);
            }
        }
    }

    private static void AuthorizeScopes(IAuthorizationPolicy authorization, PrincipalRecord principal,
        ImmutableArray<EventVectorAuthorizationScope> scopes, EventVectorAdmissionPolicy admission,
        EventVectorReadLifetimeBinding? lifetimeBinding)
    {
        if (lifetimeBinding is null)
        {
            EventVectorParentScopes.Authorize(authorization, principal, scopes, admission);
        }
        else
        {
            lifetimeBinding.Authorize(authorization, principal, scopes, admission);
        }
    }
}
