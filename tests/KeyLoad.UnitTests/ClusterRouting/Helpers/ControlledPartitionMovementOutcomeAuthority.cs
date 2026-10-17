using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Identity;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Retains immutable original control-owned outcome evidence for genuine movement operations.</summary>
internal sealed class ControlledPartitionMovementOutcomeAuthority
{
    private readonly byte[] outcomeKey;
    private readonly byte[]? locatorKey;
    private readonly byte[] originalOutcome;
    private readonly byte[]? originalLocator;

    internal ControlledPartitionMovementOutcomeAuthority(ControlledPartitionMovementNode control,
        ReplicatedOperation original)
    {
        var normalized = control.Database.NormalizeOperation(original);
        var scope = CommandOutcomePartitionIdentity.Resolve(normalized);
        outcomeKey = scope.Kind switch
        {
            CommandOutcomeScopeKind.Global => KeySpace.GlobalOutcome(original.PrincipalId, original.Id),
            CommandOutcomeScopeKind.Unknown => KeySpace.UnknownOutcome(original.PrincipalId, original.Id),
            _ => KeySpace.PartitionOutcome(scope.Partition!, original.PrincipalId, original.Id)
        };
        locatorKey = scope.Partition is { } partition
            ? KeySpace.OutcomeLocatorV2(partition, original.PrincipalId, original.Id) : null;
        originalOutcome = control.Store.Read(view => view.ReadOwnedValue(outcomeKey))
            ?? throw new InvalidOperationException("The original control outcome is absent.");
        originalLocator = locatorKey is null ? null
            : control.Store.Read(view => view.ReadOwnedValue(locatorKey));
    }

    internal async Task RemainsControlOwnedAsync(ControlledPartitionMovementNode control,
        ControlledPartitionMovementNode destination)
    {
        var retained = control.Store.Read(view => view.ReadOwnedValue(outcomeKey));
        await Assert.That(retained is not null && retained.AsSpan().SequenceEqual(originalOutcome)).IsTrue();
        await Assert.That(destination.Store.Read(view => view.ReadOwnedValue(outcomeKey))).IsNull();
        if (locatorKey is { } key)
        {
            var locator = control.Store.Read(view => view.ReadOwnedValue(key));
            await Assert.That(originalLocator is null ? locator is null
                : locator is not null && locator.AsSpan().SequenceEqual(originalLocator)).IsTrue();
            await Assert.That(destination.Store.Read(view => view.ReadOwnedValue(key))).IsNull();
        }
    }
}
