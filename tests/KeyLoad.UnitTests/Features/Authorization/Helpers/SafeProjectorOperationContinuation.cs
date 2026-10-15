namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationContinuation
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationHistory history)
    {
        var patch = SafeProjectorOperationMutations.Patch(owner, SafeProjectorOperationProtocol.InitialRevision,
            SafeProjectorOperationProtocol.UpdatedStatus, false, SafeProjectorOperationProtocol.WithdrawnUseEpoch);
        history.PublicPatch = patch;
        history.Commands.Add(patch);
        await SafeProjectorOperationMutations.PatchReceiptAsync(patch, SafeProjectorOperationProtocol.UpdatedRevision);
        await history.CurrentAsync(owner, SafeProjectorOperationProtocol.WithdrawnUseEpoch, []);
        await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.WithdrawnUseEpoch, false, owner.CallerToken, patch);
        await history.SetAsync(owner, SafeProjectorOperationProtocol.RawReadEpoch, [SafeProjectorOperationProtocol.ReadGrant]);
        await ReadOnlyGrantAsync(owner, history);
        await history.SetAsync(owner, SafeProjectorOperationProtocol.FullWriteEpoch,
            [SafeProjectorOperationProtocol.ReadGrant, SafeProjectorOperationProtocol.UseGrant, SafeProjectorOperationProtocol.WriteGrant]);
        await SafeProjectorOperationRows.WaveAsync(owner, SafeProjectorOperationProtocol.UpdatedJson,
            SafeProjectorOperationProtocol.UpdatedSafe, SafeProjectorOperationProtocol.UpdatedStatus,
            SafeProjectorOperationProtocol.UpdatedRevision, true, owner.CallerToken);
        await SafeProjectorOperationAuthority.UsageAsync(owner, true, SafeProjectorOperationProtocol.UpdatedJson,
            SafeProjectorOperationProtocol.UpdatedRevision, true, owner.CallerToken);
        var healthy = SafeProjectorOperationMutations.Patch(owner, SafeProjectorOperationProtocol.UpdatedRevision,
            SafeProjectorOperationProtocol.HealthyStatus, true, SafeProjectorOperationProtocol.FullWriteEpoch);
        history.HealthyPatch = healthy;
        history.Commands.Add(healthy);
        await SafeProjectorOperationMutations.PatchReceiptAsync(healthy, SafeProjectorOperationProtocol.HealthyRevision);
        await history.CurrentAsync(owner, SafeProjectorOperationProtocol.FullWriteEpoch,
            [SafeProjectorOperationProtocol.ReadGrant, SafeProjectorOperationProtocol.UseGrant, SafeProjectorOperationProtocol.WriteGrant]);
        await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.FullWriteEpoch, true, owner.CallerToken, patch, healthy);
        await history.SetAsync(owner, SafeProjectorOperationProtocol.FinalMaskedEpoch, [SafeProjectorOperationProtocol.WriteGrant]);
        await FinalAsync(owner, history);
        await SafeProjectorOperationRefusals.RoundtripAsync(owner, history,
            SafeProjectorOperationProtocol.FinalMaskedEpoch, SafeProjectorOperationProtocol.HealthyRevision);
        await history.CurrentAsync(owner, SafeProjectorOperationProtocol.FinalMaskedEpoch, [SafeProjectorOperationProtocol.WriteGrant]);
    }

    internal static async Task FinalAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationHistory history)
    {
        await SafeProjectorOperationRows.WaveAsync(owner, SafeProjectorOperationProtocol.HealthyJson,
            SafeProjectorOperationProtocol.HealthySafe, SafeProjectorOperationProtocol.HealthyStatus,
            SafeProjectorOperationProtocol.HealthyRevision, false, owner.CallerToken);
        await SafeProjectorOperationAuthority.UsageAsync(owner, false, SafeProjectorOperationProtocol.HealthySafe,
            SafeProjectorOperationProtocol.HealthyRevision, false, owner.CallerToken);
        await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.FinalMaskedEpoch, false, owner.CallerToken, history.PublicPatch, history.HealthyPatch);
    }

    private static async Task ReadOnlyGrantAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationHistory history)
    {
        await SafeProjectorOperationRows.WaveAsync(owner, SafeProjectorOperationProtocol.UpdatedJson,
            SafeProjectorOperationProtocol.UpdatedSafe, SafeProjectorOperationProtocol.UpdatedStatus,
            SafeProjectorOperationProtocol.UpdatedRevision, true, owner.CallerToken);
        await SafeProjectorOperationAuthority.UsageAsync(owner, false, SafeProjectorOperationProtocol.UpdatedJson,
            SafeProjectorOperationProtocol.UpdatedRevision, true, owner.CallerToken);
        await SafeProjectorOperationRefusals.ProtectedPatchAsync(owner, history,
            SafeProjectorOperationProtocol.RawReadEpoch, SafeProjectorOperationProtocol.UpdatedRevision);
        await history.CurrentAsync(owner, SafeProjectorOperationProtocol.RawReadEpoch, [SafeProjectorOperationProtocol.ReadGrant]);
        await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.RawReadEpoch, true, owner.CallerToken, history.PublicPatch);
    }
}
