namespace KeyLoad.IntegrationTests.TimeSeries;

internal sealed record SampleChunkSubnormalClusterState(
    SampleChunkClusterScenario Scenario,
    CommandRequest Open, CommitReceipt OpenReceipt,
    CommandRequest Append, CommitReceipt AppendReceipt,
    CommandRequest Seal, CommitReceipt SealReceipt,
    CommandRequest Correction, CommitReceipt CorrectionReceipt)
{
    internal (CommandRequest Command, CommitReceipt Receipt)[] Originals =>
    [(Open, OpenReceipt), (Append, AppendReceipt), (Seal, SealReceipt), (Correction, CorrectionReceipt)];
}
