using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationPolicyReplayAssertions
{
    private const string PolicyChanged = "The principal policy changed since this command was evaluated.";
    private const string AdministrationRequired = "Cluster administration is required.";
    private const string MissingOutcome = "The original registration policy outcome is missing.";

    internal static byte[] Outcome(PhysicalShardCatalogFixture fixture, Guid id)
        => fixture.Store.Read(view => view.ReadOwnedValue(KeyLoad.Core.KeySpace.GlobalOutcome(
            PhysicalOwnerRegistrationPolicyReplayFlow.PrincipalId, id)) ?? throw new InvalidOperationException(MissingOutcome));

    internal static async Task PolicyReplayAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, byte[] bytes)
    {
        var result = fixture.Database.Apply(original);
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.SafeDetail).IsEqualTo(PolicyChanged);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(Outcome(fixture, original.Id).SequenceEqual(bytes)).IsTrue();
    }

    internal static async Task AdministrationReplayAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, byte[] bytes)
    {
        await AdministrationDeniedAsync(fixture.Database.Apply(original));
        await Assert.That(Outcome(fixture, original.Id).SequenceEqual(bytes)).IsTrue();
    }

    internal static async Task AdministrationDeniedAsync(OperationResult result)
    {
        await Assert.That(result.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(result.SafeDetail).IsEqualTo(AdministrationRequired);
        await Assert.That(result.Json).IsNull();
        await Assert.That(result.NativeValue).IsNull();
    }

    internal static async Task StoredAsync(PhysicalShardCatalogFixture fixture, ReplicatedOperation operation,
        OperationResult result, byte[] bytes, long policyEpoch)
    {
        await Assert.That(Outcome(fixture, operation.Id).SequenceEqual(bytes)).IsTrue();
        var stored = NativeSerialization.Deserialize<StoredOutcome>(bytes);
        await Assert.That(stored.PolicyEpoch).IsEqualTo(policyEpoch);
        await Assert.That(stored.Incarnation).IsEqualTo(fixture.Store.Identity.Incarnation);
        await Assert.That(NativeSerialization.Serialize(stored.Result).SequenceEqual(NativeSerialization.Serialize(result))).IsTrue();
    }

    internal static async Task OnlyOutcomeAndEvaluatedClockAddedAsync(PhysicalShardCatalogFixture fixture, string[] before, Guid id, DateTimeOffset evaluatedAt)
    {
        var prefix = Convert.ToHexString(KeyLoad.Core.KeySpace.GlobalOutcome(
            PhysicalOwnerRegistrationPolicyReplayFlow.PrincipalId, id)) + ":";
        var clockPrefix = Convert.ToHexString(KeyLoad.Core.KeySpace.ClockBytes) + ":";
        await Assert.That(before.Count(row => row.StartsWith(clockPrefix, StringComparison.Ordinal))).IsEqualTo(1);
        var recordedClock = clockPrefix + Convert.ToHexString(NativeSerialization.Serialize(evaluatedAt));
        var expected = before.Select(row => row.StartsWith(clockPrefix, StringComparison.Ordinal) ? recordedClock : row);
        var after = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        await Assert.That(after.Where(row => !row.StartsWith(prefix, StringComparison.Ordinal)).SequenceEqual(expected)).IsTrue();
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
