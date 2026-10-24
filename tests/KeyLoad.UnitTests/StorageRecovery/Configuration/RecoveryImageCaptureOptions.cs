namespace KeyLoad.UnitTests.StorageRecovery;

/// <summary>Bounds complete execution recovery-fixture image observation; a remaining row must refuse the proof.</summary>
[ConfigurationOptions]
internal sealed class RecoveryImageCaptureOptions
{
    private const int RecordCeiling = 1024;
    private const string Invalid = "Recovery image capture exceeds its bounded fixture policy.";

    internal int MaximumRecords { get; init; } = RecordCeiling;

    internal void Validate()
    {
        if (MaximumRecords <= 0 || MaximumRecords > RecordCeiling)
        {
            throw new InvalidOperationException(Invalid);
        }
    }
}
