using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationCanonical
{
    private const string Documents = "document";
    private const string Indexes = "index";
    private const string Unique = "unique";
    private const string DocumentEpoch = "document-epoch";
    private const string MissingEpoch = "The actual safe projector document epoch is absent.";
    private const string Separator = ":";
    private const string SecondStatus = "second";
    private const long InitialEpoch = 1;
    private const long UpdatedEpoch = 2;
    private const long HealthyEpoch = 3;

    internal static async Task CurrentAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationState state,
        PrincipalRecord principal, SafeProjectorOperationCommand? patch = null, SafeProjectorOperationCommand? healthy = null)
    {
        var active = healthy ?? patch ?? state.Seed;
        var revision = healthy is not null ? SafeProjectorOperationProtocol.HealthyRevision
            : patch is not null ? SafeProjectorOperationProtocol.UpdatedRevision : SafeProjectorOperationProtocol.InitialRevision;
        var json = healthy is not null ? SafeProjectorOperationProtocol.HealthyJson
            : patch is not null ? SafeProjectorOperationProtocol.UpdatedJson : SafeProjectorOperationProtocol.InitialJson;
        var status = healthy is not null ? SafeProjectorOperationProtocol.HealthyStatus
            : patch is not null ? SafeProjectorOperationProtocol.UpdatedStatus : SafeProjectorOperationProtocol.InitialStatus;
        var epoch = healthy is not null ? HealthyEpoch : patch is not null ? UpdatedEpoch : InitialEpoch;
        var first = owner.Source.ExpectedReference(new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First));
        var second = owner.Source.ExpectedReference(new(owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Second));
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<DocumentRecord>(KeySpace.Partition(
            Documents, owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.First))),
            new DocumentRecord(first, revision, json, new RowAccess(), new DateTimeOffset(active.Operation.Timestamp)));
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<DocumentRecord>(KeySpace.Partition(
            Documents, owner.Source.Partition, SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Second))),
            new DocumentRecord(second, SafeProjectorOperationProtocol.InitialRevision, SafeProjectorOperationProtocol.SecondJson,
                new RowAccess(), new DateTimeOffset(state.Seed.Operation.Timestamp)));
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(
            owner.Source.Partition.TenantId, owner.Source.Partition.DatabaseId, SafeProjectorOperationProtocol.Collection))), state.Resource);
        await SafeProjectorOperationSeed.EqualAsync(owner.Store.Read(view => view.GetRecord<PrincipalRecord>(
            KeySpace.Principal(SafeProjectorOperationProtocol.Reader))), principal);
        var encodedEpoch = owner.Store.Read(view => view.ReadOwnedValue(KeySpace.Partition(DocumentEpoch,
            owner.Source.Partition, SafeProjectorOperationProtocol.Collection))) ?? throw new InvalidOperationException(MissingEpoch);
        await Assert.That(NativeSerialization.Deserialize<long>(encodedEpoch)).IsEqualTo(epoch);
        await Assert.That(Rows(owner, Documents).Length).IsEqualTo(SafeProjectorOperationProtocol.BothRows);
        await Assert.That(Rows(owner, Unique)).IsEmpty();
        await Assert.That(Rows(owner, Indexes)).IsEquivalentTo(new[]
        {
            Index(owner, status, SafeProjectorOperationProtocol.First),
            Index(owner, SecondStatus, SafeProjectorOperationProtocol.Second)
        }.Order(StringComparer.Ordinal).ToArray(), TUnit.Assertions.Enums.CollectionOrdering.Matching);
    }

    private static string Index(SafeProjectorOperationDatabase owner, string status, string id)
        => Convert.ToHexString(KeySpace.Partition(Indexes, owner.Source.Partition,
            SafeProjectorOperationProtocol.Collection, SafeProjectorOperationProtocol.Index, status, id))
            + Separator + Convert.ToHexString(NativeSerialization.Serialize(id));

    private static string[] Rows(SafeProjectorOperationDatabase owner, string family) => owner.Store.Read(view =>
    {
        var page = view.Scan(KeySpace.Partition(family, owner.Source.Partition, SafeProjectorOperationProtocol.Collection),
            owner.Database.Limits.MaxScanRecords);
        if (page.HasMore)
        {
            throw new InvalidOperationException(IncompleteImage);
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + Separator + Convert.ToHexString(row.Value.Span)).ToArray();
    });

    private const string IncompleteImage = "The safe projector canonical image exceeded its configured read bound.";
}
