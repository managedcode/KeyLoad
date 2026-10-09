using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class RuntimeJournalReaderFenceTests
{
    private const int UnsupportedReaderContract = StoreReaderContract.RuntimeJournal + 1;

    [Test]
    [Arguments(StoreReaderContract.Unspecified)]
    [Arguments(UnsupportedReaderContract)]
    public async Task AcNative002ZeroOrUnknownCapabilityRejectsWithoutFileMutation(int capability)
    {
        using var fixture = new RuntimeJournalReaderFixture();
        StoreIdentity originalIdentity;
        long originalPosition;
        using (var created = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(created.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(created, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            originalIdentity = created.Identity;
            originalPosition = created.Position;
        }

        var originalBytes = await File.ReadAllBytesAsync(
            RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath));
        await RuntimeJournalReaderFixture.RewriteCapabilityAsync(fixture.CanonicalPath, capability);
        var before = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = fixture.Open(fixture.CanonicalPath);
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var after = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        await Assert.That(after.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(before.Keys.Order(StringComparer.Ordinal),
            CollectionOrdering.Matching);
        foreach (var file in before)
        {
            await Assert.That(after[file.Key]).IsEquivalentTo(file.Value, CollectionOrdering.Matching);
        }
        await RuntimeJournalReaderRepairContinuation.RequireAsync(fixture, originalIdentity, originalPosition,
            originalBytes, TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }

    [Test]
    public async Task AcNative002OmittedCurrentCapabilityRejectsWithoutMutationAndRecovers()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        Guid nodeId;
        using (var created = fixture.Open(fixture.CanonicalPath))
        {
            nodeId = created.Identity.NodeId;
            RuntimeJournalReaderFixture.WriteRuntimeRecord(created, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
        }

        var identityPath = RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath);
        var currentIdentity = await File.ReadAllBytesAsync(identityPath);
        await RuntimeJournalReaderFixture.OmitReaderCapabilityAsync(fixture.CanonicalPath);
        var before = await RuntimeJournalReaderFixture.ReadInventoryAsync(fixture.CanonicalPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = fixture.Open(fixture.CanonicalPath);
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await AssertInventoryUnchangedAsync(fixture.CanonicalPath, before);
        await File.WriteAllBytesAsync(identityPath, currentIdentity);
        using (var restored = fixture.Open(fixture.CanonicalPath))
        {
            await Assert.That(restored.Identity.NodeId).IsEqualTo(nodeId);
            await Assert.That(restored.Identity.MinimumReaderContract).IsEqualTo(StoreReaderContract.RuntimeJournal);
            await Assert.That(RuntimeJournalReaderFixture.Read(restored, RuntimeJournalReaderFixture.RuntimeJournalKey))
                .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue);
            RuntimeJournalReaderFixture.WriteRuntimeRecord(restored, RuntimeJournalReaderFixture.FollowupKey,
                RuntimeJournalReaderFixture.FollowupValue);
        }
        using var reopened = fixture.Open(fixture.CanonicalPath);
        await Assert.That(reopened.Identity.NodeId).IsEqualTo(nodeId);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.RuntimeJournalKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.RuntimeJournalValue);
        await Assert.That(RuntimeJournalReaderFixture.Read(reopened, RuntimeJournalReaderFixture.FollowupKey))
            .IsEquivalentTo(RuntimeJournalReaderFixture.FollowupValue);
    }

    private static async Task AssertInventoryUnchangedAsync(string directory,
        Dictionary<string, byte[]?> expected)
    {
        var actual = await RuntimeJournalReaderFixture.ReadInventoryAsync(directory);
        await Assert.That(actual.Keys.Order(StringComparer.Ordinal))
            .IsEquivalentTo(expected.Keys.Order(StringComparer.Ordinal), CollectionOrdering.Matching);
        foreach (var (path, bytes) in expected)
        {
            if (bytes is null)
            {
                await Assert.That(actual[path]).IsNull();
            }
            else
            {
                await Assert.That(actual[path]!).IsEquivalentTo(bytes, CollectionOrdering.Matching);
            }
        }
    }

    [Test]
    public async Task AcNative002UnsupportedIdentityMagicRejectsWithoutChangingNativeFiles()
    {
        using var fixture = new RuntimeJournalReaderFixture();
        StoreIdentity originalIdentity;
        long originalPosition;
        using (var created = fixture.Open(fixture.CanonicalPath))
        {
            RuntimeJournalReaderFixture.WriteRuntimeRecord(created, RuntimeJournalReaderFixture.RuntimeJournalKey,
                RuntimeJournalReaderFixture.RuntimeJournalValue);
            originalIdentity = created.Identity;
            originalPosition = created.Position;
        }

        var originalBytes = await File.ReadAllBytesAsync(
            RuntimeJournalReaderFixture.IdentityPath(fixture.CanonicalPath));
        await RuntimeJournalReaderFixture.CorruptIdentityMagicAsync(fixture.CanonicalPath);
        var before = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var rejected = fixture.Open(fixture.CanonicalPath);
        });

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        var after = await RuntimeJournalReaderFixture.ReadFilesAsync(fixture.CanonicalPath);
        await Assert.That(after.Keys.Order(StringComparer.Ordinal)).IsEquivalentTo(before.Keys.Order(StringComparer.Ordinal),
            CollectionOrdering.Matching);
        foreach (var file in before)
        {
            await Assert.That(after[file.Key]).IsEquivalentTo(file.Value, CollectionOrdering.Matching);
        }
        await RuntimeJournalReaderRepairContinuation.RequireAsync(fixture, originalIdentity, originalPosition,
            originalBytes, TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
    }
}
