using Microsoft.Extensions.Options;
namespace KeyLoad.Server;

internal static class ServerNodeUpgradeProgressFile
{
    internal static void Seal(string stage, ServerNodeUpgradeOwner owner, NodeFormatUpgradeStage boundary, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions, Action<NodeFormatUpgradeStage>? observer = null)
    {
        const string TemporaryFileExtension = ".tmp";

        var inventory = Capture(stage, executionOptions: executionOptions);
        var temporary = Path.Combine(stage, ServerNodeUpgradeProtocol.ProgressReceipt + TemporaryFileExtension);
        ServerNodeUpgradeReceiptFile.Write(temporary, new ServerNodeUpgradeProgress(
            ServerNodeUpgradeProtocol.ProgressFormatVersion, owner, inventory.Sha256, (int)boundary,
            owner.SourceEpoch, owner.TargetEpoch), ServerNodeUpgradeProtocol.ProgressMagic, executionOptions: executionOptions);
        File.Move(temporary, Path.Combine(stage, ServerNodeUpgradeProtocol.ProgressReceipt), overwrite: true);
        observer?.Invoke(boundary);
    }

    internal static void Verify(string stage, ServerNodeUpgradeOwner owner, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        var progress = ReadBound(stage, owner, executionOptions: executionOptions);
        if (Capture(stage, executionOptions: executionOptions).Sha256 != progress.InventorySha256)
        { throw Errors.Fail(ErrorCode.Corruption, ServerNodeUpgradeProtocol.Corrupt); }
    }

    internal static void VerifyCompleted(string directory, ServerNodeUpgradeOwner owner, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        if (ReadBound(directory, owner, executionOptions: executionOptions).StageCode != (int)NodeFormatUpgradeStage.TargetVerified)
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
    }

    private static ServerNodeUpgradeProgress ReadBound(string directory, ServerNodeUpgradeOwner owner, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
    {
        const int StageCodeEmptyCount = 0;
        const int StageCodeValidationBound = 5;

        var progress = ServerNodeUpgradeReceiptFile.Read<ServerNodeUpgradeProgress>(
            Path.Combine(directory, ServerNodeUpgradeProtocol.ProgressReceipt), ServerNodeUpgradeProtocol.ProgressMagic, executionOptions: executionOptions);
        if (progress.FormatVersion != ServerNodeUpgradeProtocol.ProgressFormatVersion
            || progress.SourceOwner != owner || progress.SourceEpoch != owner.SourceEpoch
            || progress.TargetEpoch != owner.TargetEpoch || progress.StageCode is < StageCodeEmptyCount or > StageCodeValidationBound
            || !ServerNodeUpgradeReceiptValidation.IsDigest(progress.InventorySha256))
        { throw Errors.Fail(ErrorCode.FormatUnsupported, ServerNodeUpgradeProtocol.Invalid); }
        return progress;
    }

    private static ServerNodeUpgradeInventory Capture(string stage, IOptions<ServerNodeUpgradeExecutionOptions> executionOptions)
        => ServerNodeUpgradeInventory.Capture(stage, excludePreparedReceipt: true, excludeProgressReceipt: true, executionOptions: executionOptions);
}
