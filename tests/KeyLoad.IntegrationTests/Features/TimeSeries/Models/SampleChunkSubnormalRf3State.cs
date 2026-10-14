namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed record SampleChunkSubnormalRf3State(
    SampleChunkRf3Scenario Scenario,
    AtomicPartitionPlacementResolution Placement,
    CommandRequest Open, CommitReceipt OpenReceipt,
    CommandRequest Append, CommitReceipt AppendReceipt,
    CommandRequest Seal, CommitReceipt SealReceipt,
    CommandRequest Correction, CommitReceipt CorrectionReceipt)
{
    internal (CommandRequest Command, CommitReceipt Receipt)[] Originals =>
    [(Open, OpenReceipt), (Append, AppendReceipt), (Seal, SealReceipt), (Correction, CorrectionReceipt)];
}
