using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationIdentityOutcomeAssertions
{
    private const string IdentityConflict = "The registered physical owner identity conflicts with its existing entry.";
    private const string CommandConflict = "The command ID was already used with different content.";
    private const string MissingOutcome = "The original owner registration outcome is missing.";
    private const long RootPolicyEpoch = 1;

    internal static async Task RefusedAsync(OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.SafeDetail).IsEqualTo(IdentityConflict);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }

    internal static async Task CommandConflictAsync(OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(result.SafeDetail).IsEqualTo(CommandConflict);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }

    internal static byte[] Outcome(PhysicalShardCatalogFixture fixture, Guid id)
        => fixture.Store.Read(view => view.ReadOwnedValue(KeyLoad.Core.KeySpace.GlobalOutcome(
            PhysicalShardCatalogFixture.RootPrincipalId, id)) ?? throw new InvalidOperationException(MissingOutcome));

    internal static async Task StoredAsync(PhysicalShardCatalogFixture fixture, Guid id,
        OperationResult result, byte[] original)
    {
        await Assert.That(Outcome(fixture, id).SequenceEqual(original)).IsTrue();
        var stored = NativeSerialization.Deserialize<StoredOutcome>(original);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(RootPolicyEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(NativeSerialization.Serialize(stored.Result).SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
    }

    internal static async Task ReplayAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation operation, OperationResult result)
        => await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(operation))
            .SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();

    internal static async Task OnlyOutcomeAddedAsync(PhysicalShardCatalogFixture fixture,
        string[] before, Guid id)
    {
        var prefix = Convert.ToHexString(KeyLoad.Core.KeySpace.GlobalOutcome(
            PhysicalShardCatalogFixture.RootPrincipalId, id)) + ":";
        var after = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        await Assert.That(after.Where(row => !row.StartsWith(prefix, StringComparison.Ordinal))
            .SequenceEqual(before)).IsTrue();
        await Assert.That(after.Count(row => row.StartsWith(prefix, StringComparison.Ordinal))).IsEqualTo(1);
    }

    internal static async Task AuthorityAsync(PhysicalShardCatalogFixture fixture, byte[] directory, byte[] catalog)
    {
        await Assert.That(PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture).SequenceEqual(directory)).IsTrue();
        await Assert.That(fixture.ReadCatalogBytes().SequenceEqual(catalog)).IsTrue();
        await Assert.That(NativeSerialization.Serialize(fixture.Database.ReadPhysicalOwnerDirectory(
            PhysicalShardCatalogFixture.RootPrincipalId)).SequenceEqual(
            NativeSerialization.Serialize(PhysicalOwnerDirectoryWholeFlow.Expected()))).IsTrue();
    }
}
