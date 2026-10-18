using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeBoundLeaseProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeBoundLeaseSeed
{
    internal static async Task<NativeBoundLeaseSeeded> CreateAsync(
        RequestCqrsHostedRuntimeFixture fixture, RequestCqrsHostedRuntimeFixture other, CancellationToken token)
    {
        var owner = fixture.Database;
        var id = Guid.NewGuid();
        var request = new ConfigureResourceRequest(K.Partition.TenantId, K.Partition.DatabaseId,
            new(K.Collection, ResourceKind.Collection, K.Partition.TransactionDomainId));
        _ = (await owner.Coordinator.SubmitNativeAsync(OperationKind.ConfigureResource, id, K.Root,
            NativeSerialization.Serialize(request), cancellationToken: token)).Get<ResourceDefinition>();
        var creation = OutcomeStoreOracle.ReadStored(owner.Database, id, token).PreparedCommand;
        var writeId = Guid.NewGuid();
        var write = new CommandRequest(writeId, K.Partition,
            [new PutDocument(K.Collection, K.FirstId, K.FirstJson, K.Empty),
                new PutDocument(K.Collection, K.SecondId, K.SecondJson, K.Empty)]);
        await owner.Coordinator.ReadBarrierAsync(token);
        var next = checked(owner.Database.LastApplied + K.NextIndex);
        var receipt = (await owner.Coordinator.SubmitNativeAsync(OperationKind.Batch, writeId, K.Root,
            NativeSerialization.Serialize(write), cancellationToken: token)).Get<CommitReceipt>();
        var original = OutcomeStoreOracle.ReadStored(owner.Database, writeId, token).PreparedCommand;
        var physical = fixture.PhysicalServices.GetRequiredService<PhysicalShardRecord>();
        var expected = new CommitReceipt(writeId, new(physical.Incarnation, K.Partition.AtomicPartitionId, next),
            [new(K.MutationKind, K.Collection, K.FirstId, K.Revision),
                new(K.MutationKind, K.Collection, K.SecondId, K.Revision)], DurabilityProfile.ProcessDurable, K.PersistedCopies);
        await NativeBoundLeaseAssertions.EqualAsync(expected, receipt);
        var images = new Dictionary<RequestCqrsHostedRuntimeFixture, (long AppliedPosition, long Position, string[] Records)>
        {
            [fixture] = Image(fixture),
            [other] = Image(other)
        };
        return new(fixture, other, physical, creation, original, receipt, images);
    }

    internal static (long AppliedPosition, long Position, string[] Records) Image(RequestCqrsHostedRuntimeFixture fixture)
        => fixture.Database.Store.Read(view =>
        {
            var page = view.Scan([], K.MaximumImageRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException(K.ImageLimit);
            }
            var applied = view.ReadOwnedValue(KeySpace.AppliedBytes) is { } bytes
                ? NativeSerialization.Deserialize<long>(bytes) : K.Empty;
            return (applied, fixture.Database.Store.Position, page.Records.Select(record =>
                Convert.ToHexString(record.Key.Span) + K.RecordSeparator + Convert.ToHexString(record.Value.Span)).ToArray());
        });

    internal static async Task UnchangedAsync(RequestCqrsHostedRuntimeFixture fixture,
        (long AppliedPosition, long Position, string[] Records) expected)
    {
        var actual = Image(fixture);
        await Assert.That(actual.AppliedPosition).IsEqualTo(expected.AppliedPosition);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Records.SequenceEqual(expected.Records, StringComparer.Ordinal)).IsTrue();
    }
}
