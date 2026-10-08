using System.Security.Cryptography;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.Search;

internal static class NativeTextSeedCollector
{
    private const int PlacementVersion = 1;
    private const long EmptyPosition = 0;
    private const string InvalidScope = "The text projection source scope is inconsistent.";
    private const string OwnerLost = "The text projection owner no longer matches the canonical placement.";
    private const string SeedExceeded = "The text projection seed exceeds its record budget.";
    private static readonly string[] MutationKinds = [MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument];

    internal static NativeTextSeedCapture Capture(DatabaseEngine database, string principalId,
        NativeTextSeedPin pin, ReadExecutionBudget budget)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(pin);
        ArgumentNullException.ThrowIfNull(budget);
        budget.Check();
        return database.Store.Read(view => CaptureView(database, view, principalId, pin, budget));
    }

    internal static NativeTextSeedCapture CaptureView(DatabaseEngine database, IKeyValueView raw,
        string principalId, NativeTextSeedPin pin, ReadExecutionBudget budget)
    {
        var view = budget.CreateView(raw);
        var principal = database.Principal(view, principalId, database.EvaluationClock.GetUtcNow());
        var partition = pin.Consumer.Partition;
        var consumer = DatabaseEngine.RequireActiveProjectionConsumer(view, principal, pin.Consumer, pin.Generation);
        if (!consumer.Definition.Resources.SequenceEqual([pin.Collection], StringComparer.Ordinal)
            || !consumer.Definition.MutationKinds.SequenceEqual(MutationKinds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidScope); }
        RequireOwner(database, view, principalId, pin);
        database.Authorization.Require(principal, partition, pin.Collection, Capability.Query | Capability.DocumentsRead);
        var resource = database.Resource(view, partition, pin.Collection, ResourceKind.Collection);
        database.Authorization.RequireFieldUse(principal, resource, pin.Field);
        var head = database.ReadOutboxHead(view, partition);
        DatabaseEngine.RequireNativeTextProjectionHistory(head, consumer.Checkpoint);
        var appliedBytes = view.ReadOwnedValue(KeySpace.AppliedBytes) ?? throw Errors.Fail(ErrorCode.Corruption, InvalidScope);
        var applied = NativeSerialization.Deserialize<long>(appliedBytes);
        if (applied < EmptyPosition || consumer.Checkpoint > head.Tail)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidScope); }
        var documents = new List<DocumentRecord>();
        var range = budget.VisitRange(raw, DocumentStorageKeys.Prefix(partition, pin.Collection),
            database.Limits.MaxScanRecords, (key, value) =>
            {
                budget.ChargeBytes(value.Length);
                var document = NativeSerialization.Deserialize<DocumentRecord>(value);
                if (document.Reference.Partition != partition || document.Reference.Collection != pin.Collection
                    || document.Revision <= EmptyPosition
                    || !key.SequenceEqual(DocumentStorageKeys.RecordKey(document.Reference)))
                { throw Errors.Fail(ErrorCode.Corruption, InvalidScope); }
                documents.Add(document);
                return true;
            });
        if (range.HasMore)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, SeedExceeded); }
        budget.Check();
        var identity = database.Store.Identity;
        return new(principal.Id, principal.PolicyEpoch, resource.SchemaVersion,
            database.Store.Position, applied, head.Tail, head.FirstAvailable, consumer.Checkpoint,
            identity.Incarnation, identity.FormatVersion, identity.ReadGeneration, ResourceDigest(resource, budget), documents.ToArray());
    }

    internal static void RequireReleased(DatabaseEngine database, string principalId,
        NativeTextSeedPin pin, ReadExecutionBudget budget)
    {
        budget.Check();
        _ = database.Store.Read(view =>
        {
            var bounded = budget.CreateView(view);
            var principal = database.Principal(bounded, principalId, database.EvaluationClock.GetUtcNow());
            var state = DatabaseEngine.RequireReleasedProjectionConsumer(bounded, principal, pin.Consumer, pin.Generation);
            if (!state.Definition.Resources.SequenceEqual([pin.Collection], StringComparer.Ordinal)
                || !state.Definition.MutationKinds.SequenceEqual(MutationKinds, StringComparer.Ordinal))
            { throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidScope); }
            RequireOwner(database, bounded, principalId, pin);
            budget.Check();
            return true;
        });
    }

    private static string ResourceDigest(ResourceDefinition resource, ReadExecutionBudget budget)
    {
        budget.ChargeBytes(NativeSerialization.Measure(resource));
        budget.ChargeBytes(SHA256.HashSizeInBytes);
        var bytes = NativeSerialization.Serialize(resource);
        budget.Check();
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static void RequireOwner(DatabaseEngine database, IKeyValueView view,
        string principalId, NativeTextSeedPin pin)
    {
        var actual = database.ReadAtomicPartitionPlacement(view, principalId, new(PlacementVersion, pin.Consumer.Partition));
        var expected = pin.Placement;
        if (database.Store.Identity.NodeId != pin.NodeId
            || database.Store.Identity.Incarnation != expected.Incarnation
            || actual.PhysicalShardId != expected.PhysicalShardId || actual.Incarnation != expected.Incarnation
            || actual.PlacementEpoch != expected.PlacementEpoch
            || !actual.VoterIds.SequenceEqual(expected.VoterIds, StringComparer.Ordinal))
        { throw Errors.Fail(ErrorCode.OwnershipLost, OwnerLost); }
    }
}
