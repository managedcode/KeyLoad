namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed class ClusterRestoreRf3OperatorHistory
{
    internal List<int> Exits { get; } = [];
    internal List<ClusterRestoreRf3OperatorObservation> Observations { get; } = [];
    internal int? LastExit
    {
        get; private set;
    }

    internal void RecordExit(int value)
    {
        LastExit = value;
        Exits.Add(value);
    }
}
