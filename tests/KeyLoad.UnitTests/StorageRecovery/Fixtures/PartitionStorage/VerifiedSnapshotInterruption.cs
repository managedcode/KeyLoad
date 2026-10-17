namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class VerifiedSnapshotInterruption : Exception
{
    public VerifiedSnapshotInterruption()
    {
    }
    public VerifiedSnapshotInterruption(string message) : base(message)
    {
    }
    public VerifiedSnapshotInterruption(string message, Exception innerException) : base(message, innerException)
    {
    }
}
