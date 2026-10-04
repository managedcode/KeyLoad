using System.Text.Json;
using KeyLoad.CrashHost;

namespace KeyLoad.RecoveryTests.Features.StorageRecovery;

internal static class EpochPriorExecutableFixture
{
    private const int ExpectedDataEpoch = 5;
    private const string InvalidReceipt = "The actual prior-executable receipt does not match its frozen source or operation.";

    internal static async Task<EpochPriorProbeReceipt> CreateAsync(string directory, bool compacted,
        CancellationToken cancellationToken)
    {
        var receipt = await InvokeAsync(new(directory, EpochPriorSourceProbe.CreateOperation, compacted), cancellationToken);
        if (receipt.ErrorCode is not null || receipt.DataEpoch != ExpectedDataEpoch
            || receipt.NodeId == Guid.Empty || receipt.Incarnation == Guid.Empty)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        return receipt;
    }

    internal static async Task<EpochPriorProbeReceipt> CreateNodeAsync(string directory, EpochPriorNodeProfile profile,
        CancellationToken cancellationToken)
    {
        var receipt = await InvokeAsync(new(directory, EpochPriorSourceProbe.CreateNodeOperation,
            NodeProfile: profile), cancellationToken);
        if (receipt.ErrorCode is not null || receipt.DataEpoch != ExpectedDataEpoch
            || receipt.NodeId == Guid.Empty || receipt.Incarnation != profile.Incarnation || receipt.AppliedPosition != 3)
        { throw new InvalidDataException(InvalidReceipt); }
        return receipt;
    }

    internal static Task<EpochPriorProbeReceipt> InspectAsync(string directory, CancellationToken cancellationToken)
        => InvokeAsync(new(directory, EpochPriorSourceProbe.InspectOperation), cancellationToken);

    internal static Task<EpochPriorProbeReceipt> VerifySnapshotAsync(string directory, string snapshot,
        CancellationToken cancellationToken)
        => InvokeAsync(new(directory, EpochPriorSourceProbe.VerifySnapshotOperation, Snapshot: snapshot), cancellationToken);

    private static async Task<EpochPriorProbeReceipt> InvokeAsync(EpochPriorSourceRequest request,
        CancellationToken cancellationToken)
    {
        var executable = await EpochPriorExecutableArtifact.VerifyAsync(cancellationToken);
        var result = await EpochPriorExecutableProcess.RunAsync(executable,
            JsonSerializer.Serialize(request, EpochPriorSourceProbe.JsonOptions), cancellationToken);
        var receipt = JsonSerializer.Deserialize<EpochPriorProbeReceipt?>(result.Output, EpochPriorSourceProbe.JsonOptions)
            ?? throw new InvalidDataException(InvalidReceipt);
        if (receipt.SourceRevision != EpochPriorSourceProbe.SourceRevision
            || result.ExitCode != (receipt.ErrorCode is null ? 0 : 1) || result.Error.Length != 0)
        {
            throw new InvalidDataException(InvalidReceipt);
        }
        return receipt;
    }
}
