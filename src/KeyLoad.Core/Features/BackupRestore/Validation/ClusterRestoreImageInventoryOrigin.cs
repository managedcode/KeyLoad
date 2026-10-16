using KeyLoad.Core.Features.ResourceExecution;

namespace KeyLoad.Core;

/// <summary>Independently verifies the exact D2 origin rewrite from the authenticated original archive view.</summary>
internal static class ClusterRestoreImageInventoryOrigin
{
    private const string Invalid = "The recovered resource owner inventory origin differs from its exact admitted transformation.";

    internal static void Require(ClusterRestoreImageState state)
    {
        state.Work.Check();
        var source = ResourceOwnerInventoryCatalog.ReadOrigin(state.Source, state.Limits.Value);
        if (state.Local.Source.Incarnation != state.Context.SourceIncarnation
            || state.Local.Target.Incarnation != state.Context.TargetIncarnation
            || source?.Revision == long.MaxValue
            || source is { Proven: true } && source.StoreIncarnation != state.Context.SourceIncarnation)
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        // Reconcile always writes this row. Proven origins bind the fresh target; unknown origins retain their preimage identity.
        var expected = new ResourceOwnerInventoryOriginV1(ResourceOwnerInventoryProtocol.Version,
            source is { Proven: true } ? state.Local.Target.Incarnation : source?.StoreIncarnation ?? Guid.Empty,
            source?.Proven ?? false,
            (source?.Revision ?? ResourceOwnerInventoryProtocol.Zero) + ResourceOwnerInventoryProtocol.Next);
        var bytes = ResourceOwnerInventoryValidation.Encode(expected, state.Limits.Value);
        var actual = state.Target.ReadOwnedValue(ResourceOwnerInventoryKeys.Origin());
        if (actual is null || !bytes.AsSpan().SequenceEqual(actual))
        {
            throw Errors.Fail(ErrorCode.Corruption, Invalid);
        }
        state.Work.Check();
    }
}
