namespace KeyLoad.ProcessRecoveryTests;

/// <summary>Deletes a settled storage trial root once the killed owner's handles are released.</summary>
internal static class StorageTrialCleanup
{
    internal static async Task DeleteTrialAsync(string root, CancellationToken cancellationToken)
    {
        var clock = TimeProvider.System;
        var started = clock.GetTimestamp();
        while (true)
        {
            try
            {
                Directory.Delete(root, true);
                return;
            }
            catch (IOException) when (clock.GetElapsedTime(started) < RecoveryExecutionOptions.ProcessReadiness().Value.OwnershipTimeout)
            {
                await Task.Delay(RecoveryExecutionOptions.ProcessReadiness().Value.OwnershipPollInterval, clock, cancellationToken);
            }
        }
    }
}
