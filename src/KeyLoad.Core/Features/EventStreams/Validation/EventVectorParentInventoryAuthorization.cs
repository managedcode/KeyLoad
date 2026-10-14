namespace KeyLoad.Core;

internal static class EventVectorParentInventoryAuthorization
{
    internal static void Require(IAuthorizationPolicy authorization, PrincipalRecord principal, EventVectorOwnedInventory inventory,
        EventVectorAdmissionPolicy admission)
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
                EventVectorParentScopes.Authorize(authorization, principal, parent.OriginalAuthorizedSourceScopes, admission);
            }
            else if (row.Key.Span.SequenceEqual(cleanupKey))
            {
                var cleanup = EventVectorCleanupValidation.Read(map, row, admission);
                EventVectorParentScopes.Authorize(authorization, principal, cleanup.OriginalAuthorizedSourceScopes, admission);
            }
        }
    }
}
