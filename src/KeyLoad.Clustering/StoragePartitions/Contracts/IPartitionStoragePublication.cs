namespace KeyLoad.Clustering;

/// <summary>Observes the original silo-local physical opening without acquiring another storage owner.</summary>
public interface IPartitionStoragePublication
{
    /// <summary>Gets the same physical opening task, or null when execution storage initialization never started.</summary>
    Task? Opening
    {
        get;
    }

    /// <summary>Waits for the original physical publication; cancellation ends only this caller's wait.</summary>
    /// <param name="cancellationToken">Cancellation of this waiting caller, never the physical opening.</param>
    /// <returns>Completion after the original opening succeeds while its owner remains available.</returns>
    Task WaitForOpenAsync(CancellationToken cancellationToken);
}
