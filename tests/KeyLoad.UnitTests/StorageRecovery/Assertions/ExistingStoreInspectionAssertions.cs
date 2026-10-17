using KeyLoad.CrashHost;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class ExistingStoreInspectionAssertions
{
    private const int SuccessfulExitCode = 0;
    private const int ExpectedSchemaVersion = 1;
    private const int NoProcessId = 0;
    private const int Sha256CharacterCount = 64;
    private const int PipeRetentionLimit = 8192;
    private const string MissingReceiptMessage = "The inspector child did not return its bounded receipt.";

    internal static async Task SucceededAsync(ExistingStoreInspectorExit result, StoreIdentity expectedIdentity,
        byte[] expectedValue, long expectedPosition)
    {
        var receipt = await ReceiptAsync(result, successful: true);
        await Assert.That(receipt.NodeId).IsEqualTo(expectedIdentity.NodeId);
        await Assert.That(receipt.Incarnation).IsEqualTo(expectedIdentity.Incarnation);
        await Assert.That(receipt.FormatVersion).IsEqualTo(ZoneTreeExistingStoreFixture.CurrentFormat);
        await Assert.That(receipt.Position).IsEqualTo(expectedPosition);
        await Assert.That(receipt.Value).IsEquivalentTo(expectedValue, CollectionOrdering.Matching);
        await Assert.That(receipt.FailureTypes).IsEmpty();
        await Assert.That(receipt.ErrorCode).IsNull();
    }

    internal static async Task FailedAsync(ExistingStoreInspectorExit result, string? errorCode,
        string? failureType = null)
    {
        var receipt = await ReceiptAsync(result, successful: false);
        await Assert.That(receipt.ErrorCode).IsEqualTo(errorCode);
        if (failureType is not null)
        {
            await Assert.That(receipt.FailureTypes.FirstOrDefault()).IsEqualTo(failureType);
        }
    }

    internal static async Task SettledAsync(ExistingStoreInspectorExit result)
    {
        await Assert.That(result.ProcessId).IsGreaterThan(NoProcessId);
        await Assert.That(result.AssemblySha256.Length).IsEqualTo(Sha256CharacterCount);
        await Assert.That(result.Stdout.Length).IsLessThanOrEqualTo(PipeRetentionLimit);
        await Assert.That(result.Stderr.Length).IsLessThanOrEqualTo(PipeRetentionLimit);
        await Assert.That(result.ProcessReaped).IsTrue();
        await Assert.That(result.StdoutReaderSettled).IsTrue();
        await Assert.That(result.StderrReaderSettled).IsTrue();
        await Assert.That(result.OuterOwnerReleased).IsTrue();
    }

    private static async Task<ExistingStoreInspectionReceipt> ReceiptAsync(
        ExistingStoreInspectorExit result, bool successful)
    {
        await SettledAsync(result);
        await Assert.That(result.ExitCode).IsEqualTo(SuccessfulExitCode);
        await Assert.That(result.Canceled).IsFalse();
        await Assert.That(result.Receipt).IsNotNull();
        var receipt = result.Receipt ?? throw new InvalidOperationException(MissingReceiptMessage);
        await Assert.That(receipt.SchemaVersion).IsEqualTo(ExpectedSchemaVersion);
        await Assert.That(receipt.Success).IsEqualTo(successful);
        return receipt;
    }
}
