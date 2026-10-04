using KeyLoad.CrashHost;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class ZoneTreeExistingStoreValidationTests
{
    private const byte ChecksumCorruptionMask = 0x01;
    private const int NoObservedStages = 0;
    private const long NoRetainedBytes = 0;

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcSg009001CapturedNodeOrIncarnationMismatchDoesNotChangeIdentity(bool incarnation)
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var expectedNodeId = incarnation ? files.Identity.NodeId : Guid.NewGuid();
        var configuredIncarnation = incarnation ? Guid.NewGuid() : files.Identity.Incarnation;
        var result = await files.InspectAsync(expectedNodeId: expectedNodeId, incarnation: configuredIncarnation);
        await ExistingStoreInspectionAssertions.FailedAsync(result, ExistingStoreInspectionExpectedFailures.TokenInvalidated, ExistingStoreInspectionExpectedFailures.KeyLoad);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009001LegacyIdentityIsRejectedWithoutPromotion()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        ZoneTreeIdentityFile.Write(files.IdentityPath, files.Identity with { FormatVersion = ZoneTreeExistingStoreFixture.LegacyFormat });
        var original = await File.ReadAllBytesAsync(files.IdentityPath);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, ExistingStoreInspectionExpectedFailures.FormatUnsupported, ExistingStoreInspectionExpectedFailures.KeyLoad);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(original, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009001CorruptOriginalIdentityIsRejectedWithoutReplacement()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var original = await File.ReadAllBytesAsync(files.IdentityPath);
        var envelope = NativeSerialization.Deserialize<ZoneTreeIdentityEnvelope>(original.AsSpan(sizeof(ulong)));
        envelope.Checksum[0] ^= ChecksumCorruptionMask;
        var corrupted = ZoneTreeMetadataBinary.Write(envelope, 0x354449444C4BUL);
        await File.WriteAllBytesAsync(files.IdentityPath, corrupted);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, ExistingStoreInspectionExpectedFailures.Corruption, ExistingStoreInspectionExpectedFailures.KeyLoad);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(corrupted, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009001InvalidOptionsAreRejectedBeforeOpeningOriginalOwner()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.NullOptions, ExistingStoreInspectionExpectedFailures.ArgumentNull);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.EmptyNodeId, ExistingStoreInspectionExpectedFailures.Argument);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.MissingIncarnation, ExistingStoreInspectionExpectedFailures.Argument);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.EmptyIncarnation, ExistingStoreInspectionExpectedFailures.Argument);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.RelativeDirectory, ExistingStoreInspectionExpectedFailures.Argument);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.EmptyDirectory, ExistingStoreInspectionExpectedFailures.Argument);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.ZeroFrameBudget, ExistingStoreInspectionExpectedFailures.ArgumentOutOfRange);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.ZeroSnapshotBudget, ExistingStoreInspectionExpectedFailures.ArgumentOutOfRange);
        await AssertInvalidAsync(files, ExistingStoreInspectionVariant.NonCanonicalDirectory, ExistingStoreInspectionExpectedFailures.Argument);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009001CacheAndObserverAreRejectedWithoutAdmissionOrInvocation()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var cache = await files.InspectAsync(ExistingStoreInspectionVariant.Cache);
        await ExistingStoreInspectionAssertions.FailedAsync(cache, null, ExistingStoreInspectionExpectedFailures.Argument);
        await Assert.That(cache.Receipt!.ObservedStages).IsEqualTo(NoObservedStages);
        await Assert.That(cache.Receipt.RetainedBytes).IsEqualTo(NoRetainedBytes);
        var observer = await files.InspectAsync(ExistingStoreInspectionVariant.Observer);
        await ExistingStoreInspectionAssertions.FailedAsync(observer, null, ExistingStoreInspectionExpectedFailures.Argument);
        await Assert.That(observer.Receipt!.ObservedStages).IsEqualTo(NoObservedStages);
        await Assert.That(observer.Receipt.RetainedBytes).IsEqualTo(NoRetainedBytes);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    private static async Task AssertInvalidAsync(ZoneTreeExistingStoreFixture files,
        ExistingStoreInspectionVariant variant, string exceptionType)
    {
        var result = await files.InspectAsync(variant);
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, exceptionType);
    }
}
