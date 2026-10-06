using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class EpochStorageUpgradeTests
{
    private const int SourcePosition = 1;
    private const string UnknownFormatScenario = "unknown-identity";
    private const string CorruptScenario = "corrupt-journal";
    private const string TornScenario = "torn-journal";
    private const string LinkScenario = "linked-journal";
    private const string StageScenario = "mismatched-stage";
    private const string MissingIdentityScenario = "missing-identity";
    private const string MissingJournalScenario = "missing-journal";
    private const string AuthorityScenario = "authority-mismatch";
    private const string NonemptyDestinationScenario = "nonempty-destination";
    private const string UnknownStageFile = "unowned.bin";
    private const string UnrelatedDestinationFile = "unrelated.bin";

    [Test]
    [Arguments(false, EpochStorageFixture.Native5Epoch)]
    [Arguments(true, EpochStorageFixture.Native5Epoch)]
    [Arguments(false, EpochStorageFixture.Native6Epoch)]
    [Arguments(true, EpochStorageFixture.Native6Epoch)]
    public async Task AcEpoch002OfflineUpgradeCopiesNativeAuthorityDataAndPositions(bool checkpoint, int sourceEpoch)
    {
        using var fixture = new EpochStorageFixture();
        var original = await fixture.CreateNativeSourceAsync(checkpoint, sourceEpoch);
        var sourceFiles = await EpochStorageFixture.CaptureAsync(fixture.Source);
        var upgraded = ZoneTreeFormatUpgrade.Upgrade(fixture.Source, fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution());
        await EpochStorageFixture.AssertIdentityPreservedAsync(original, upgraded);
        await Assert.That(upgraded.FormatVersion).IsEqualTo(EpochStorageFixture.CurrentEpoch);
        using (var destination = new ZoneTreeStore(fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            await Assert.That(destination.Position).IsEqualTo(SourcePosition);
            var records = destination.Read(view => new[]
            {
                view.ReadOwnedValue(fixture.FirstKey),
                view.ReadOwnedValue(fixture.SecondKey),
                view.ReadOwnedValue(KeyCodec.Encode("system", "last-applied"))
            });
            await Assert.That(records[0]!).IsEquivalentTo(fixture.FirstValue, CollectionOrdering.Matching);
            await Assert.That(records[1]!).IsEquivalentTo(fixture.SecondValue, CollectionOrdering.Matching);
            await Assert.That(NativeSerialization.Deserialize<long>(records[2]!)).IsEqualTo(SourcePosition);
        }
        await EpochStorageFixture.AssertUnchangedAsync(fixture.Source, sourceFiles);
    }

    [Test]
    [Arguments(EpochStorageFixture.Native5Epoch)]
    [Arguments(EpochStorageFixture.Native6Epoch)]
    [Arguments(8)]
    public async Task AcEpoch001OrdinaryCurrentOpenRejectsOldAndUnknownBeforeChangingFiles(int version)
    {
        using var fixture = new EpochStorageFixture();
        var sourceEpoch = version is EpochStorageFixture.Native5Epoch or EpochStorageFixture.Native6Epoch
            ? version : EpochStorageFixture.Native5Epoch;
        var identity = await fixture.CreateNativeSourceAsync(checkpoint: false, sourceEpoch);
        if (version != sourceEpoch)
        {
            ZoneTreeIdentityFile.Write(Path.Combine(fixture.Source, "identity.json"), identity with { FormatVersion = version }, UnitExecutionOptions.StorageExecution().Value.IdentityBufferBytes);
        }
        var before = await EpochStorageFixture.CaptureAsync(fixture.Source);
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
        {
            using var store = new ZoneTreeStore(fixture.SourceOptions, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        });
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.FormatUnsupported);
        await EpochStorageFixture.AssertUnchangedAsync(fixture.Source, before);
    }

    [Test]
    [Arguments(CorruptScenario, ErrorCode.Corruption)]
    [Arguments(TornScenario, ErrorCode.Corruption)]
    [Arguments(UnknownFormatScenario, ErrorCode.FormatUnsupported)]
    [Arguments(LinkScenario, ErrorCode.FormatUnsupported)]
    [Arguments(StageScenario, ErrorCode.FormatUnsupported)]
    [Arguments(MissingIdentityScenario, ErrorCode.FormatUnsupported)]
    [Arguments(MissingJournalScenario, ErrorCode.FormatUnsupported)]
    [Arguments(AuthorityScenario, ErrorCode.TokenInvalidated)]
    [Arguments(NonemptyDestinationScenario, ErrorCode.Conflict)]
    public async Task AcEpoch002InvalidSourceOrStagingFailsWithoutPublication(string scenario,
        ErrorCode expectedError)
    {
        using var fixture = new EpochStorageFixture();
        var identity = await fixture.CreateNativeSourceAsync(checkpoint: false);
        await ConfigureFailureAsync(fixture, identity, scenario);
        var before = await EpochStorageFixture.CaptureAsync(fixture.Source);
        var upgradeStage = fixture.Destination + EpochStorageFixture.UpgradeStageSuffix;
        var stageBefore = Directory.Exists(upgradeStage)
            ? await EpochStorageFixture.CaptureAsync(upgradeStage) : null;
        var options = fixture.DestinationOptions(identity);
        if (scenario == AuthorityScenario)
        {
            options = options with { Incarnation = Guid.NewGuid() };
        }
        if (scenario == NonemptyDestinationScenario)
        {
            Directory.CreateDirectory(fixture.Destination);
            await File.WriteAllBytesAsync(Path.Combine(fixture.Destination, UnrelatedDestinationFile), [0x01]);
        }
        var destinationBefore = Directory.Exists(fixture.Destination)
            ? await EpochStorageFixture.CaptureAsync(fixture.Destination) : null;
        var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
            ZoneTreeFormatUpgrade.Upgrade(fixture.Source, options, UnitExecutionOptions.StorageExecution()));
        await Assert.That(rejected.Code).IsEqualTo(expectedError);
        if (destinationBefore is null)
        {
            await Assert.That(Directory.Exists(fixture.Destination)).IsFalse();
        }
        else
        {
            await EpochStorageFixture.AssertUnchangedAsync(fixture.Destination, destinationBefore);
        }
        await EpochStorageFixture.AssertUnchangedAsync(fixture.Source, before);
        if (stageBefore is not null)
        {
            await EpochStorageFixture.AssertUnchangedAsync(upgradeStage, stageBefore);
        }
    }

    [Test]
    public async Task AcEpoch003RetryOfPublishedUpgradePreservesLaterTargetWrites()
    {
        using var fixture = new EpochStorageFixture();
        var original = await fixture.CreateNativeSourceAsync(checkpoint: true);
        var first = ZoneTreeFormatUpgrade.Upgrade(fixture.Source, fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution());
        using (var destination = new ZoneTreeStore(fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution()))
        {
            destination.Commit((transaction, _) =>
            {
                transaction.Put([0x41, 0x00], [0x99, 0x10]);
                return true;
            });
        }
        var retried = ZoneTreeFormatUpgrade.Upgrade(fixture.Source, fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution());
        await EpochStorageFixture.AssertIdentityPreservedAsync(first, retried);
        using var reopened = new ZoneTreeStore(fixture.DestinationOptions(original), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Position).IsEqualTo(SourcePosition + 1);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue([0x41, 0x00]))!)
            .IsEquivalentTo(new byte[] { 0x99, 0x10 }, CollectionOrdering.Matching);
    }

    private static Task ConfigureFailureAsync(EpochStorageFixture fixture, StoreIdentity identity,
        string scenario) => scenario switch
        {
            UnknownFormatScenario => SetUnknownIdentityAsync(fixture, identity),
            CorruptScenario => RewriteJournalAsync(fixture, torn: false),
            TornScenario => RewriteJournalAsync(fixture, torn: true),
            LinkScenario => LinkJournalAsync(fixture),
            StageScenario => CreateMismatchedStageAsync(fixture),
            MissingIdentityScenario => DeleteArtifact(fixture, "identity.json"),
            MissingJournalScenario => DeleteArtifact(fixture, EpochStorageFixture.JournalName),
            _ => Task.CompletedTask
        };

    private static Task SetUnknownIdentityAsync(EpochStorageFixture fixture, StoreIdentity identity)
    {
        ZoneTreeIdentityFile.Write(Path.Combine(fixture.Source, "identity.json"), identity with { FormatVersion = 8 }, UnitExecutionOptions.StorageExecution().Value.IdentityBufferBytes);
        return Task.CompletedTask;
    }

    private static async Task RewriteJournalAsync(EpochStorageFixture fixture, bool torn)
    {
        var path = Path.Combine(fixture.Source, EpochStorageFixture.JournalName);
        var bytes = await File.ReadAllBytesAsync(path);
        if (torn)
        {
            Array.Resize(ref bytes, bytes.Length - 1);
        }
        else
        {
            bytes[^1] ^= 1;
        }
        await File.WriteAllBytesAsync(path, bytes);
    }

    private static Task LinkJournalAsync(EpochStorageFixture fixture)
    {
        var journal = Path.Combine(fixture.Source, EpochStorageFixture.JournalName);
        var target = journal + ".owned";
        File.Move(journal, target);
        File.CreateSymbolicLink(journal, target);
        return Task.CompletedTask;
    }

    private static async Task CreateMismatchedStageAsync(EpochStorageFixture fixture)
    {
        var staging = fixture.Destination + EpochStorageFixture.UpgradeStageSuffix;
        Directory.CreateDirectory(staging);
        await File.WriteAllBytesAsync(Path.Combine(staging, UnknownStageFile), [0x01, 0x02]);
    }

    private static Task DeleteArtifact(EpochStorageFixture fixture, string name)
    {
        File.Delete(Path.Combine(fixture.Source, name));
        return Task.CompletedTask;
    }

}
