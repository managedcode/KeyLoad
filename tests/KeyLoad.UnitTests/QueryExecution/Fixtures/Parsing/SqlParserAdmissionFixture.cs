using KeyLoad.UnitTests.Features.Authorization;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal sealed class SqlParserAdmissionFixture : IDisposable
{
    internal static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness
    {
        get;
    } = new(new()
    {
        MaxQueryBytes = 4096,
        MaxQueryTokens = 128,
        MaxQueryDepth = 16
    },
timeProvider: new ControlledReadClock(Epoch));
    internal List<(CommandRequest Request, CommitReceipt Receipt, byte[] Outcome)> Additional
    {
        get;
    } = [];
    internal CommandRequest Seed
    {
        get;
        private set;
    } = null!;
    internal CommitReceipt Receipt
    {
        get;
        private set;
    } = null!;
    internal byte[] Outcome
    {
        get;
        private set;
    } = null!;
    internal async Task InitializeAsync()
    {
        const long ResourceConfigurationsAndSeed = 3L;
        var next = Harness.ReadJournalCut().LastIndex + ResourceConfigurationsAndSeed;
        var seeded = SqlOffsetFixture.Seed(Harness);
        Seed = seeded.Seed;
        Receipt = new(Seed.CommandId, new(Harness.Store.Identity.Incarnation, Harness.Partition.AtomicPartitionId, next, 1L),
            [.. Seed.Mutations.Cast<PutDocument>().Select(value => new MutationReceipt("putDocument", value.Collection, value.Id, 1L))],
            DurabilityProfile.ProcessDurable);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(Receipt, seeded.Receipt);
        await Assert.That(Harness.ReadJournalCut().LastIndex).IsEqualTo(next);
        Outcome = RequiredProcessingPolicyAssertions.Outcome(Harness.Store, Harness.Partition, "root", Seed.CommandId);
        await SqlParserAdmissionModels.ConfigureAsync(this);
        await PolicyAsync(1L, true);
    }
    internal async Task PolicyAsync(long epoch, bool allow)
    {
        var principal = SqlOffsetFixture.Principal(Harness, epoch, allow);
        if (allow)
        {
            principal = principal with
            {
                Grants = [.. principal.Grants,
                new(Harness.Partition.DatabaseId, SqlParserAdmissionModels.Events, Capability.Query | Capability.EventsRead),
                new(Harness.Partition.DatabaseId, SqlParserAdmissionModels.Table, Capability.Query | Capability.DocumentsRead)]
            };
        }
        await RequiredProcessingPolicyAssertions.EqualAsync(principal, Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
    }
    public void Dispose() => Harness.Dispose();
}
