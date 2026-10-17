namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal sealed record ClusterRestoreRf3SecondaryState(CommandRequest Command, CommitReceipt Receipt,
    DocumentResult Document)
{
    public override string ToString() => nameof(ClusterRestoreRf3SecondaryState);
}
