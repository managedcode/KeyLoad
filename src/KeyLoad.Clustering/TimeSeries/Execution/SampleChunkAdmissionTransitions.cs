using KeyLoad.Core.Features.TimeSeries;

namespace KeyLoad.Clustering;

internal static class SampleChunkAdmissionTransitions
{
    internal static async Task RetireSettledAsync(BackgroundStorageClient storage,
        Func<IEnumerable<KeyValuePair<Guid, SampleChunkJobAdmission>>> snapshot,
        Func<Guid, CancellationToken, Task> remove, CancellationToken token)
    {
        foreach (var entry in snapshot())
        {
            SampleChunkWorkHint? current;
            try
            {
                current = await storage.Operations.ReadSampleChunkCurrentAsync(entry.Value.Hint, token).ConfigureAwait(true);
            }
            catch (KeyLoadException failure) when (failure.Code is ErrorCode.Unauthenticated or ErrorCode.PermissionDenied)
            {
                continue;
            }
            if (current is null || current.Revision != entry.Value.Hint.Revision
                || entry.Value.BlockedPolicyEpoch is { } epoch && current.CreatorPolicyEpoch != epoch)
            {
                await remove(entry.Key, token).ConfigureAwait(true);
            }
        }
    }

    internal static SampleChunkJobAdmission Blocked(SampleChunkJobAdmission original, string jobId, ErrorCode code)
        => original with
        {
            JobId = jobId,
            BlockedPolicyEpoch = original.Hint.CreatorPolicyEpoch,
            SettledError = code
        };
}
