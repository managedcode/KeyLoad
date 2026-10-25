using System.Buffers.Binary;
using System.Text.Json;
using KeyLoad.ProcessRecoveryTests;
using KeyLoad.Storage.ZoneTree;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class ZoneTreeExistingStoreTests
{
    private const string DirectoryJsonProperty = "\"directory\":";
    private const string DuplicateDirectoryProperty = "\"directory\":\"duplicate\",\"directory\":";
    private const string NullDirectoryProperty = "\"directory\":null,";
    private const string UnknownProperty = ",\"unknown\":true}";
    private const string PropertySeparator = ",";
    private const string FinalObjectToken = "}";
    private const string NormalVariantJson = "\"variant\":\"Normal\"";
    private const string NumericVariant = "\"variant\":0";
    private const string UndefinedVariant = "\"variant\":\"Undefined\"";
    private const string MalformedJson = "{";
    private const string ParentDirectorySegment = "..";
    private const char OversizedJsonCharacter = ' ';
    private const int MaximumProtocolCharacters = 4096;
    private const int ProtocolFailureExitCode = 2;
    private const int JournalPayloadLengthOffset = sizeof(ulong);
    private const int CorruptPayloadLength = int.MaxValue;

    [Test]
    public async Task AcSg009P001NonCanonicalDotAndDotDotDirectoryAreRejectedBeforeGuard()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var dot = await files.InspectAsync(ExistingStoreInspectionVariant.NonCanonicalDirectory,
            directory: files.DirectoryPath);
        await ExistingStoreInspectionAssertions.FailedAsync(dot, null, ExistingStoreInspectionExpectedFailures.Argument);
        var parent = Path.GetDirectoryName(files.DirectoryPath)!;
        var parentAndChild = await files.InspectAsync(directory: Path.Combine(parent, ParentDirectorySegment,
            Path.GetFileName(parent), Path.GetFileName(files.DirectoryPath)));
        await ExistingStoreInspectionAssertions.FailedAsync(parentAndChild, null, ExistingStoreInspectionExpectedFailures.Argument);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009P004WaitBeforeOpenCancellationReapsChildAndReadersBeforeReopen()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var result = await files.InspectAsync(ExistingStoreInspectionVariant.WaitBeforeOpen, cancelWhenReady: true);
        await ExistingStoreInspectionAssertions.SettledAsync(result);
        await Assert.That(result.Canceled).IsTrue();
        await Assert.That(result.Receipt).IsNull();
        await Assert.That(result.Stdout).IsEqualTo(string.Empty);
        await Assert.That(result.Stderr).Contains(ExistingStoreInspectorProcess.ReadyMarker);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key))).IsEquivalentTo(files.Value, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcSg009P003StrictProtocolRejectsMalformedDuplicateMissingNullUnknownNumericUndefinedAndOversizedInput()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var originalFiles = await files.CaptureStoreEntriesAsync();
        var canonicalRequest = new ExistingStoreInspectionRequest(files.DirectoryPath, files.Identity.NodeId,
            files.Identity.Incarnation, ExistingStoreInspectionVariant.Normal);
        var validJson = JsonSerializer.Serialize(canonicalRequest, ExistingStoreInspectorProtocol.JsonOptions);
        var requests = new[]
        {
            MalformedJson,
            DuplicateProperty(validJson),
            MissingProperty(validJson),
            NullDirectory(validJson),
            AddUnknownProperty(validJson),
            ReplaceVariant(validJson, NumericVariant),
            ReplaceVariant(validJson, UndefinedVariant),
            new string(OversizedJsonCharacter, MaximumProtocolCharacters + 1)
        };

        foreach (var request in requests)
        {
            var result = await ExistingStoreInspectorProcess.RunRawAsync(request, files.OuterOwnerPath);
            await ExistingStoreInspectionAssertions.SettledAsync(result);
            await Assert.That(result.ExitCode).IsEqualTo(ProtocolFailureExitCode);
            await Assert.That(result.Receipt).IsNull();
            await Assert.That(result.Stdout).IsEqualTo(string.Empty);
            await Assert.That(result.Stderr).IsEqualTo(string.Empty);
            await files.AssertOwnerAvailableAsync();
        }

        await files.AssertStoreEntriesUnchangedAsync(originalFiles);
        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key))).IsEquivalentTo(files.Value, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcSg009P003ExplicitNullIncarnationReachesRealGuardAndReturnsItsFailureFacts()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var request = new ExistingStoreInspectionRequest(files.DirectoryPath, files.Identity.NodeId,
            null, ExistingStoreInspectionVariant.Normal);
        var result = await ExistingStoreInspectorProcess.RunAsync(request, files.OuterOwnerPath);
        await ExistingStoreInspectionAssertions.FailedAsync(result, null, ExistingStoreInspectionExpectedFailures.Argument);
        await files.AssertOwnerAvailableAsync();
    }

    private static string DuplicateProperty(string json)
        => json.Replace(DirectoryJsonProperty, DuplicateDirectoryProperty, StringComparison.Ordinal);

    private static string MissingProperty(string json)
        => json.Replace(DirectoryPropertyWithValue(json), string.Empty, StringComparison.Ordinal);

    private static string NullDirectory(string json)
        => json.Replace(DirectoryPropertyWithValue(json), NullDirectoryProperty, StringComparison.Ordinal);

    private static string AddUnknownProperty(string json)
        => json.Replace(FinalObjectToken, UnknownProperty, StringComparison.Ordinal);

    private static string DirectoryPropertyWithValue(string json)
    {
        var start = json.IndexOf(DirectoryJsonProperty, StringComparison.Ordinal);
        var end = json.IndexOf(PropertySeparator, start, StringComparison.Ordinal);
        return json[start..(end + PropertySeparator.Length)];
    }

    private static string ReplaceVariant(string json, string replacement)
        => json.Replace(NormalVariantJson, replacement, StringComparison.Ordinal);
    private const long CommittedPosition = 1;
    private const byte IncompleteTailByte = 0xA5;

    [Test]
    public async Task AcSg009002OriginalIdentityDataPositionAndDoubleDisposePermitNormalReopen()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.SucceededAsync(result, files.Identity, files.Value, CommittedPosition);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Position).IsEqualTo(CommittedPosition);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key))).IsEquivalentTo(files.Value, CollectionOrdering.Matching);
    }

    [Test]
    public async Task AcSg009002IncompleteCanonicalTailRecoversWithoutChangingIdentityOrCommittedData()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var journalBytes = await File.ReadAllBytesAsync(files.JournalPath);
        await File.WriteAllBytesAsync(files.JournalPath, [.. journalBytes, IncompleteTailByte]);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.SucceededAsync(result, files.Identity, files.Value, CommittedPosition);
        await Assert.That(await File.ReadAllBytesAsync(files.JournalPath)).IsEquivalentTo(journalBytes, CollectionOrdering.Matching);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
    }

    [Test]
    public async Task AcSg009003CurrentJournalLengthCorruptionReleasesOwnerAndAllowsRepairedReopen()
    {
        using var files = new ZoneTreeExistingStoreFixture();
        var identityBytes = await File.ReadAllBytesAsync(files.IdentityPath);
        var journalBytes = await File.ReadAllBytesAsync(files.JournalPath);
        var corruptedJournal = journalBytes.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(
            corruptedJournal.AsSpan(JournalPayloadLengthOffset), CorruptPayloadLength);
        await File.WriteAllBytesAsync(files.JournalPath, corruptedJournal);
        var result = await files.InspectAsync();
        await ExistingStoreInspectionAssertions.FailedAsync(result, ExistingStoreInspectionExpectedFailures.Corruption, ExistingStoreInspectionExpectedFailures.KeyLoad);
        await Assert.That(await File.ReadAllBytesAsync(files.IdentityPath)).IsEquivalentTo(identityBytes, CollectionOrdering.Matching);
        await files.AssertOwnerAvailableAsync();
        await File.WriteAllBytesAsync(files.JournalPath, journalBytes);
        using var reopened = new ZoneTreeStore(files.Options, UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        await Assert.That(reopened.Position).IsEqualTo(CommittedPosition);
        await Assert.That(reopened.Read(view => view.ReadOwnedValue(files.Key))).IsEquivalentTo(files.Value, CollectionOrdering.Matching);
    }
}
