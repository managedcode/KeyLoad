using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochPriorExecutableFixture
{
    private const int Native5DataEpoch = 5;
    private const string InvalidReceipt = "The actual prior-executable receipt does not match its frozen source or operation.";

    internal static async Task<EpochPriorProbeReceipt> CreateAsync(string directory, bool compacted,
        CancellationToken cancellationToken, int dataEpoch = Native5DataEpoch)
    {
        var receipt = await InvokeAsync(new(directory, EpochPriorSourceProbe.CreateOperation, compacted),
            dataEpoch, cancellationToken);
        if (receipt.ErrorCode is not null || receipt.DataEpoch != dataEpoch
            || receipt.NodeId == Guid.Empty || receipt.Incarnation == Guid.Empty)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        return receipt;
    }

    internal static async Task<EpochPriorProbeReceipt> CreateOutcomeFrameAsync(string directory, Guid commandId,
        CancellationToken cancellationToken, int dataEpoch = 6)
    {
        if (dataEpoch != 6 || commandId == Guid.Empty)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        var receipt = await InvokeAsync(new(directory, EpochPriorSourceProbe.CreateOutcomeFrameOperation,
            OutcomeCommandId: commandId), dataEpoch, cancellationToken);
        ValidateOutcomeFrame(receipt, commandId);
        return receipt;
    }

    internal static async Task AssertMissingOutcomeCommandRejectedAsync(string directory,
        CancellationToken cancellationToken)
    {
        var receipt = await InvokeAsync(new(directory, EpochPriorSourceProbe.CreateOutcomeFrameOperation),
            6, cancellationToken);
        if (receipt.DataEpoch != 0 || receipt.ErrorCode != ErrorCode.Validation.ToString()
            || receipt.OutcomeCommandId is not null || receipt.OutcomeFrameBase64 is not null
            || Directory.Exists(directory))
        {
            throw new InvalidDataException(InvalidReceipt);
        }
    }

    private static void ValidateOutcomeFrame(EpochPriorProbeReceipt receipt, Guid commandId)
    {
        if (receipt.ErrorCode is not null || receipt.DataEpoch != 6 || receipt.Position <= 0
            || receipt.NodeId == Guid.Empty || receipt.Incarnation == Guid.Empty
            || receipt.OutcomeCommandId != commandId
            || receipt.OutcomePrincipalId != EpochUpgradeFixture.OutcomePrincipalId
            || string.IsNullOrEmpty(receipt.OutcomeFrameBase64)
            || receipt.OutcomeFrameBase64.Length > EpochPriorSourceProbe.MaximumOutcomeFrameBase64Characters)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        var bytes = Convert.FromBase64String(receipt.OutcomeFrameBase64);
        if (bytes.Length is <= 0 or > EpochPriorSourceProbe.MaximumOutcomeFrameBytes
            || Convert.ToBase64String(bytes) != receipt.OutcomeFrameBase64)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
    }

    internal static async Task<EpochPriorProbeReceipt> CreateNodeAsync(string directory, EpochPriorNodeProfile profile,
        CancellationToken cancellationToken, int dataEpoch = Native5DataEpoch)
    {
        var receipt = await InvokeAsync(new(directory, EpochPriorSourceProbe.CreateNodeOperation,
            NodeProfile: profile), dataEpoch, cancellationToken);
        if (receipt.ErrorCode is not null || receipt.DataEpoch != dataEpoch
            || receipt.NodeId == Guid.Empty || receipt.Incarnation != profile.Incarnation || receipt.AppliedPosition != 3)
        { throw new InvalidDataException(InvalidReceipt); }
        return receipt;
    }

    internal static Task<EpochPriorProbeReceipt> InspectAsync(string directory, CancellationToken cancellationToken,
        int dataEpoch = Native5DataEpoch)
        => InvokeAsync(new(directory, EpochPriorSourceProbe.InspectOperation), dataEpoch, cancellationToken);

    internal static Task<EpochPriorProbeReceipt> VerifySnapshotAsync(string directory, string snapshot,
        CancellationToken cancellationToken, int dataEpoch = Native5DataEpoch)
        => InvokeAsync(new(directory, EpochPriorSourceProbe.VerifySnapshotOperation, Snapshot: snapshot),
            dataEpoch, cancellationToken);

    private static async Task<EpochPriorProbeReceipt> InvokeAsync(EpochPriorSourceRequest request,
        int dataEpoch, CancellationToken cancellationToken)
    {
        var executable = await EpochPriorExecutableArtifact.VerifyAsync(dataEpoch, cancellationToken);
        var result = await EpochPriorExecutableProcess.RunAsync(executable,
            JsonSerializer.Serialize(request, EpochPriorSourceProbe.JsonOptions), cancellationToken, dataEpoch);
        var receipt = JsonSerializer.Deserialize<EpochPriorProbeReceipt?>(result.Output, EpochPriorSourceProbe.JsonOptions)
            ?? throw new InvalidDataException(InvalidReceipt);
        if (receipt.SourceRevision != EpochPriorSourceProbe.SourceRevisionForEpoch(dataEpoch)
            || result.ExitCode != (receipt.ErrorCode is null ? 0 : 1) || result.Error.Length != 0)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        return receipt;
    }
}
