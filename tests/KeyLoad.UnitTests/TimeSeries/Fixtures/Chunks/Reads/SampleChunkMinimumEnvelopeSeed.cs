using KeyLoad.UnitTests.Features.OrleansExecution;

namespace KeyLoad.UnitTests.Features.TimeSeries;

internal static class SampleChunkMinimumEnvelopeSeed
{
    private const string ReaderId = "execution-chunk-minimum-reader";
    private const long NextPosition = 1;
    private const string AppendKind = "appendSamples";
    private const int PersistedCopies = 2;

    internal static async Task<SampleChunkMinimumEnvelopeState> RunAsync(ConnectionServerDatabase owner)
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
            var position = checked(owner.ReadCanonicalCut().AppliedPosition + NextPosition);
            var expected = new CommitReceipt(command.CommandId, new(owner.Store.Identity.Incarnation, owner.Partition.AtomicPartitionId, position), [new MutationReceipt(kinds[commands.Count], SampleChunkCanonicalFixture.Set,
                    SampleChunkCanonicalFixture.Series, revisions[commands.Count])], DurabilityProfile.ProcessDurable, PersistedCopies);
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

    internal static async Task ReplayAsync(ConnectionServerDatabase owner, SampleChunkMinimumEnvelopeState state)
    {
        foreach (var original in state.Commands)
        {
            await SampleChunkMinimumEnvelopeAssertions.ObserveAsync(owner.Store, async () =>
            {
                var replay = owner.Submit(OperationKind.Batch, original.Request,
                    id: original.Request.CommandId).Get<CommitReceipt>();
                await SampleChunkExplicitMinimumUnitReads.EqualAsync(original.Receipt, replay);
            });
        }
    }
}
