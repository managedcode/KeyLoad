using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkSignedMinimumSeed
{
    private const string ReaderId = "native-signed-chunk-minimum-reader";
    private const long NextPosition = 1;
    private const long InitialEpoch = 1;
    private const int AppendIndex = 1;
    private const string AppendKind = "appendSamples";

    internal static async Task<SampleChunkSignedMinimumState> RunAsync(ConnectionNativeServerDatabase owner)
    {
        owner.Configure(SampleAggregateTestData.Set, ResourceKind.TimeSeries);
        var window = Guid.NewGuid();
        Mutation[] mutations =
        [new OpenSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            window, SampleChunkCanonicalFixture.Start, SampleChunkCanonicalFixture.Until),
         new AppendSamples(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            SampleChunkCanonicalFixture.Initial, SampleChunkCanonicalFixture.Tags),
         new SealSampleChunkWindow(SampleChunkCanonicalFixture.Set, SampleChunkCanonicalFixture.Series,
            window, SampleChunkCanonicalFixture.AppendedRevision)];
        var kinds = new[] { SampleChunkProtocol.OpenKind, AppendKind, SampleChunkProtocol.SealKind };
        var revisions = new[] { SampleChunkCanonicalFixture.FirstRevision,
            SampleChunkCanonicalFixture.InitialSequence, SampleChunkCanonicalFixture.SealedRevision };
        var commands = new List<(CommandRequest Request, CommitReceipt Receipt)>();
        foreach (var mutation in mutations)
        {
            var command = new CommandRequest(Guid.NewGuid(), owner.Partition, [mutation]);
            var position = checked(owner.ReadJournalCut().LastIndex + NextPosition);
            var expected = new CommitReceipt(command.CommandId,
                new(owner.Store.Identity.Incarnation, owner.Partition.AtomicPartitionId, position, InitialEpoch),
                [new MutationReceipt(kinds[commands.Count], SampleChunkCanonicalFixture.Set,
                    SampleChunkCanonicalFixture.Series, revisions[commands.Count])], DurabilityProfile.QuorumProcessDurable);
            var actual = owner.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
            await SampleChunkExplicitMinimumUnitReads.EqualAsync(expected, actual);
            commands.Add((command, expected));
        }
        var reader = new PrincipalRecord(ReaderId, owner.Partition.TenantId,
            [new(owner.Partition.DatabaseId, SampleChunkCanonicalFixture.Set, Capability.SeriesRead)], []);
        reader = owner.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(reader)).Get<PrincipalRecord>();
        var request = new ReadSampleChunkWindowRequest(owner.Partition, SampleChunkCanonicalFixture.Set,
            SampleChunkCanonicalFixture.Series, window, null, null, SampleChunkCanonicalFixture.OutputLimit)
        {
            MinimumToken = commands.Last().Receipt.Token
        };
        return new(request, reader, commands.ToArray());
    }

    internal static CommitToken Earlier(SampleChunkSignedMinimumState state)
        => state.Commands[AppendIndex].Receipt.Token;
}
