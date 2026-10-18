using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.UnitTests.Features.Messaging;
using Microsoft.Extensions.DependencyInjection;
using ManagedCode.Communication.Commands;
using K = KeyLoad.UnitTests.Features.ClusterRouting.NativeLocalChildLifetimeProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeLocalChildLifetimeSeed
{
    internal static async Task<NativeLocalChildLifetimeSeeded> CreateAsync(
        RequestCqrsHostedRuntimeFixture fixture, RequestCqrsHostedRuntimeFixture other, CancellationToken token)
    {
        var owner = fixture.Database;
        var id = Guid.NewGuid();
        var request = new ConfigureResourceRequest(K.Partition.TenantId, K.Partition.DatabaseId,
            new(K.Collection, ResourceKind.Collection, K.Partition.TransactionDomainId));
        _ = (await owner.Coordinator.SubmitNativeAsync(OperationKind.ConfigureResource, id, K.Root,
            NativeSerialization.Serialize(request), cancellationToken: token)).Get<ResourceDefinition>();
        var creation = OutcomeStoreOracle.ReadStored(owner.Database, id, token).PreparedCommand;
        var principal = new PrincipalRecord(K.OtherSubject, K.Partition.TenantId,
            [new(K.Partition.DatabaseId, K.Collection, Capability.Query | Capability.DocumentsRead)], []);
        _ = (await owner.Coordinator.SubmitNativeAsync(OperationKind.ConfigurePrincipal, Guid.NewGuid(), K.Root,
            NativeSerialization.Serialize(new ConfigurePrincipalRequest(principal)), cancellationToken: token)).Get<PrincipalRecord>();
        var writeId = Guid.NewGuid();
        var write = new CommandRequest(writeId, K.Partition,
            [new PutDocument(K.Collection, K.FirstId, K.FirstJson, K.Empty),
                new PutDocument(K.Collection, K.SecondId, K.SecondJson, K.Empty)]);
        await owner.Coordinator.ReadBarrierAsync(token);
        var next = checked(owner.Database.LastApplied + K.NextIndex);
        var receipt = (await owner.Coordinator.SubmitNativeAsync(OperationKind.Batch, writeId, K.Root,
            NativeSerialization.Serialize(write), cancellationToken: token)).Get<CommitReceipt>();
        var original = OutcomeStoreOracle.ReadStored(owner.Database, writeId, token).PreparedCommand;
        var physical = fixture.Services.GetRequiredService<PhysicalShardRecord>();
        var expected = new CommitReceipt(writeId, new(physical.Incarnation, K.Partition.AtomicPartitionId, next),
            [new(K.MutationKind, K.Collection, K.FirstId, K.Revision),
                new(K.MutationKind, K.Collection, K.SecondId, K.Revision)], DurabilityProfile.ProcessDurable, 1);
        await NativeLocalChildLifetimeAssertions.EqualAsync(expected, receipt);
        var images = new Dictionary<RequestCqrsHostedRuntimeFixture, (long Position, string[] Records)>
        {
            [fixture] = Image(fixture),
            [other] = Image(other)
        };
        return new(fixture, other, physical, creation, original, receipt, images);
    }

    internal static (long Position, string[] Records) Image(RequestCqrsHostedRuntimeFixture fixture)
        => (fixture.Database.Store.Position, QueueWholeFlowStorage.Bytes(fixture.Database.Store));

    internal static async Task UnchangedAsync(RequestCqrsHostedRuntimeFixture fixture,
        (long Position, string[] Records) expected)
    {
        var actual = Image(fixture);
        await Assert.That(actual.Position).IsEqualTo(expected.Position);
        await Assert.That(actual.Records.SequenceEqual(expected.Records, StringComparer.Ordinal)).IsTrue();
    }
}
