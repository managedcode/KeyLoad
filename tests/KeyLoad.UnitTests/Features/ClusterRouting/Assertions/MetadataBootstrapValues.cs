using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class MetadataBootstrapValues
{
    private const int MaximumRows = 4_096;
    internal static async Task EqualAsync<T>(T expected, T actual)
        => await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
    internal static string[] Image(MetadataBootstrapState state) => state.Fixture.Store.Read(view =>
    {
        var page = view.Scan([], MaximumRows);
        if (page.HasMore)
        {
            throw new InvalidOperationException("The complete bootstrap image exceeded its bound.");
        }
        return page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)).ToArray();
    });
    internal static byte[] Outcome(MetadataBootstrapState state, ReplicatedOperation operation)
        => state.Fixture.Store.Read(view => view.ReadOwnedValue(operation.Kind == OperationKind.Batch
            ? KeySpace.PartitionOutcome(state.Partition, MetadataBootstrapState.Actor, operation.Id)
            : KeySpace.GlobalOutcome(MetadataBootstrapState.Actor, operation.Id)))
            ?? throw new InvalidOperationException("The complete original bootstrap outcome is missing.");
    internal static async Task ConservedAsync(MetadataBootstrapState state, string[] image, StoreIdentity identity, long position)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(Image(state))
            .IsEquivalentTo(image, CollectionOrdering.Matching), failures);
        await ServerFailureObserver.ObserveAsync(() => NativeReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            state.Fixture.Store, identity, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task ResultAsync(OperationResult expected, OperationResult actual)
    {
        await EqualAsync(expected, actual);
        await Assert.That(actual.NativeValue?.GetType()).IsEqualTo(expected.NativeValue?.GetType());
        await EqualAsync(expected.NativeValue, actual.NativeValue);
    }
    internal static async Task StoredAsync(MetadataBootstrapState state, ReplicatedOperation operation,
        OperationResult result, byte[] bytes, long epoch = 1L)
    {
        var stored = NativeSerialization.Deserialize<StoredOutcome>(bytes);
        await Assert.That(stored.Fingerprint).IsEqualTo(JsonData.Fingerprint(new
        {
            operation.Id,
            operation.Kind,
            operation.PrincipalId,
            operation.PayloadJson
        }));
        await Assert.That(stored.Incarnation).IsEqualTo(state.Fixture.Store.Identity.Incarnation);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(epoch);
        await ResultAsync(result, stored.Result);
        await Assert.That(stored.ScopeKind).IsEqualTo(CommandOutcomeScopeKind.Global);
        await Assert.That(stored.Partition).IsNull();
        await Assert.That(stored.BlobAuthority).IsNull();
        await Assert.That(stored.CompositionAuthority).IsNull();
        await Assert.That(stored.OnlineTextAuthority).IsNull();
        await Assert.That(stored.RemoteTransferFailureAuthority).IsNull();
        await Assert.That(stored.RemoteTransferAuthority).IsNull();
    }
}
