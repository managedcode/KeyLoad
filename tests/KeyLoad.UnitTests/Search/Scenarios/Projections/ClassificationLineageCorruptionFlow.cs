using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ClassificationLineageCorruptionFlow
{
    private const long InvalidEventRevision = 0;
    private const long SingleStorageCommit = 1;
    private const long FaultAndRepairCommits = 2;
    private const string CorruptLineage = "The stored vector projection lineage is corrupt.";
    private const string NativeDirectory = "classification-corruption-native-text";

    internal static async Task RunAsync()
    {
        var fixture = new EventProjectionFixture();
        try
        {
            await RunOwnedAsync(fixture);
        }
        catch (Exception primary)
        {
            try
            {
                fixture.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        fixture.Dispose();
    }

    private static async Task RunOwnedAsync(EventProjectionFixture fixture)
    {
        var request = fixture.Request();
        var commandId = Guid.NewGuid();
        var receipt = fixture.Apply(request, commandId);
        await ClassificationLineageWholeAssertions.ReceiptAsync(fixture, receipt);
        var key = fixture.LineageKey();
        var original = fixture.Harness.Store.Read(view => view.ReadOwnedValue(key))
            ?? throw new InvalidOperationException("The original lineage is absent.");
        var vector = fixture.VectorBytes() ?? throw new InvalidOperationException("The original vector is absent.");
        var lineage = fixture.Lineage() ?? throw new InvalidOperationException("The original lineage cannot be read.");
        var damaged = NativeSerialization.Serialize(lineage with
        {
            SourceEventRevision = InvalidEventRevision
        });
        var before = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var position = fixture.Harness.Store.Position;
        WriteOwnedRow(fixture, key, damaged);
        var faultPosition = checked(position + SingleStorageCommit);
        var expectedFault = ReplaceOwnedRow(before, key, original, damaged);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, expectedFault, faultPosition);
        var error = await Assert.ThrowsExactlyAsync<KeyLoadException>(fixture.SearchAsync)
            ?? throw new InvalidOperationException("The malformed lineage exposed its vector.");
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(error.Message).IsEqualTo(CorruptLineage);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, expectedFault, faultPosition);
        WriteOwnedRow(fixture, key, original);
        var repairedPosition = checked(position + FaultAndRepairCommits);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, before, repairedPosition);
        fixture.Harness.Reopen();
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, before, repairedPosition);
        await HealthyAsync(fixture);
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, original);
        await ClassificationLineageWholeAssertions.ReplayReceiptAsync(fixture.Apply(request, commandId), receipt);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, before, repairedPosition);
    }

    private static string[] ReplaceOwnedRow(string[] before, byte[] key, byte[] original, byte[] damaged)
    {
        var prefix = Convert.ToHexString(key) + ":";
        var expectedOriginal = prefix + Convert.ToHexString(original);
        var actualOriginal = before.Single(row => row.StartsWith(prefix, StringComparison.Ordinal));
        if (!string.Equals(actualOriginal, expectedOriginal, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The original lineage row does not match its captured bytes.");
        }
        return before.Select(row => string.Equals(row, expectedOriginal, StringComparison.Ordinal)
            ? prefix + Convert.ToHexString(damaged) : row).ToArray();
    }

    private static void WriteOwnedRow(EventProjectionFixture fixture, byte[] key, byte[] bytes)
        => fixture.Harness.Store.Commit((transaction, _) => { transaction.Put(key, bytes); return true; });

    private static async Task HealthyAsync(EventProjectionFixture fixture)
    {
        var projection = new NativeTextProjection(Path.Combine(fixture.Harness.DirectoryPath, NativeDirectory),
            UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits), fixture.Harness.Store.Identity.NodeId,
            UnitNativeTextOptions.Execution());
        try
        {
            await ClassificationLineageWholeAssertions.HealthyAsync(fixture, projection);
        }
        catch (Exception primary)
        {
            try
            {
                projection.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        projection.Dispose();
    }
}
