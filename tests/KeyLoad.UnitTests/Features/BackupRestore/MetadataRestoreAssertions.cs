using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.BackupRestore;

internal static class MetadataRestoreAssertions
{
    internal static async Task AssertRestored(MetadataBackupFixture fixture, string destination)
    {
        var restoredIdentity = ZoneTreeStore.Restore(fixture.BackupDirectory, destination);
        await Assert.That(restoredIdentity.Incarnation).IsNotEqualTo(fixture.OriginalIdentity.Incarnation);
        await Assert.That(restoredIdentity.SigningKey.Span.SequenceEqual(fixture.OriginalIdentity.SigningKey.Span)).IsFalse();
        await Assert.That(restoredIdentity.DispatchPaused).IsTrue();

        using var reopened = new ZoneTreeStore(new(destination));
        await Assert.That(reopened.Read(view => JsonDefaults.Deserialize<string>(
                view.ReadOwnedValue(MetadataBackupFixture.StoredKeyBytes)!)))
            .IsEqualTo(MetadataBackupFixture.ExpectedValue);
        await Assert.That(reopened.Identity.Incarnation).IsEqualTo(restoredIdentity.Incarnation);
        await Assert.That(reopened.Identity.DispatchPaused).IsTrue();
        await Assert.That(reopened.Identity.SigningKey.Span.SequenceEqual(restoredIdentity.SigningKey.Span)).IsTrue();
    }

    internal static async Task AssertUnsupported(KeyLoadException failure, string detail)
    {
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await Assert.That(failure.Message).IsEqualTo(detail);
    }
}
