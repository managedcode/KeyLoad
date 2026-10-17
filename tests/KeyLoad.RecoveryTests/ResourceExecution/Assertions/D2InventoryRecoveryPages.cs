using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2InventoryRecoveryPages
{
    internal static async Task RequireAsync(ControlledPartitionMovementNativeNode source,
        ControlledPartitionMovementNativeNode target, bool selectedCommitted, CancellationToken token)
    {
        foreach (var name in new[] { D2InventoryCrashProtocol.ResourceA, D2InventoryCrashProtocol.ResourceB,
                     D2InventoryCrashProtocol.ResourceC })
        {
            var work = Work(source, token);
            var request = source.Database.AdmitResourceOwnerInventoryRequest(D2InventoryCrashProtocol.Principal,
                Guid.NewGuid(), D2InventoryCrashProtocol.Tenant, D2InventoryCrashProtocol.Database, name,
                D2InventoryCrashProtocol.FirstPageSize, source.Database.EvaluationClock.GetUtcNow()
                    .AddSeconds(source.Database.Limits.QueryDeadlineSeconds), work);
            var control = source.Database.CaptureResourceOwnerInventoryControl(D2InventoryCrashProtocol.Principal, request, work);
            var first = Capture(source, control, D2InventoryCrashCorpus.Source, work);
            var second = Capture(target, control, D2InventoryCrashCorpus.Target, work);
            source.Database.ValidateResourceOwnerInventoryOwner(D2InventoryCrashProtocol.Principal, control, first.Cut, work);
            target.Database.ValidateResourceOwnerInventoryOwner(D2InventoryCrashProtocol.Principal, control, second.Cut, work);
            source.Database.ValidateResourceOwnerInventoryControl(D2InventoryCrashProtocol.Principal, control, work);
            var expected = name == D2InventoryCrashProtocol.ResourceC ? Array.Empty<string>()
                : selectedCommitted ? new[] { D2InventoryCrashProtocol.P0, D2InventoryCrashProtocol.P1, D2InventoryCrashProtocol.P2 }
                : new[] { D2InventoryCrashProtocol.P0, D2InventoryCrashProtocol.P1 };
            await Assert.That(first.Entries.Length).IsEqualTo(expected.Length);
            await Assert.That(second.Entries.Length).IsEqualTo(0);
            await Assert.That(second.Cut.OwnerDirectoryRevision).IsEqualTo(0L);
            var actual = first.Entries.Select(entry => entry.Partition.PartitionKey).Order(StringComparer.Ordinal).ToArray();
            await Assert.That(actual.SequenceEqual(expected, StringComparer.Ordinal)).IsTrue();
            foreach (var entry in first.Entries)
            {
                await SameAsync(control.Lifetime, entry.Lifetime);
                await SameAsync(D2InventoryCrashCorpus.Source.Owner, entry.ObservedOwner);
                await SameAsync(D2InventoryCrashCorpus.Source, entry.CurrentRoute);
                await Assert.That(entry.Partition).IsEqualTo(D2InventoryCrashProtocol.Partition(entry.Partition.PartitionKey));
                await Assert.That(entry.CurrentPlacement.PhysicalShardId).IsEqualTo(D2InventoryCrashCorpus.Source.Owner.PhysicalShardId);
                await Assert.That(entry.CurrentPlacement.Incarnation).IsEqualTo(D2InventoryCrashCorpus.Source.Owner.Incarnation);
            }
        }
    }

    private static (ResourceOwnerInventoryOwnerCutV1 Cut, ResourceOwnerInventoryEntryV1[] Entries) Capture(
        ControlledPartitionMovementNativeNode node, ResourceOwnerInventoryControlV1 control,
        RegisteredPhysicalOwnerV1 observed, ReadExecutionBudget work)
    {
        var entries = new List<ResourceOwnerInventoryEntryV1>();
        ResourceOwnerInventoryCursorV1? cursor = null;
        ResourceOwnerInventoryOwnerCutV1? cut = null;
        do
        {
            var page = node.Database.ReadResourceOwnerInventoryPage(D2InventoryCrashProtocol.Principal, control, observed, cursor, work);
            cut ??= page.Cut;
            if (entries.Count + page.Entries.Length > node.Database.Limits.MaxResults)
            {
                throw new InvalidOperationException("The bounded D2 recovery corpus exceeded native result capacity.");
            }
            entries.AddRange(page.Entries);
            cursor = page.Continuation;
        }
        while (cursor is not null);
        return (cut!, entries.ToArray());
    }

    internal static ReadExecutionBudget Work(ControlledPartitionMovementNativeNode node, CancellationToken token)
        => new(RecoveryExecutionOptions.DatabaseLimits(), node.Database.EvaluationClock, token);

    internal static async Task SameAsync<T>(T expected, T actual)
        => await Assert.That(NativeSerialization.Serialize(expected).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(actual))).IsTrue();

    internal static string[] Image(ControlledPartitionMovementNativeNode node)
        => node.Store.Read(view =>
        {
            var page = view.Scan([], node.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The bounded recovery conservation image was incomplete.");
            }
            return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
        });
}
