using KeyLoad.UnitTests.Authorization;
using KeyLoad.UnitTests.ResourceExecution;

namespace KeyLoad.UnitTests.QueryExecution;

internal sealed class SqlParserAdmissionFixture : IDisposable
{
    internal static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal TestDatabase Harness
    {
        get;
    } = new(new()
    {
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
        var next = Harness.ReadCanonicalCut().AppliedPosition + ResourceConfigurationsAndSeed;
        var seeded = SqlOffsetFixture.Seed(Harness);
        Seed = seeded.Seed;
        Receipt = new(Seed.CommandId, new(Harness.Store.Identity.Incarnation, Harness.Partition.AtomicPartitionId, next), [.. Seed.Mutations.Cast<PutDocument>().Select(value => new MutationReceipt("putDocument", value.Collection, value.Id, 1L))], DurabilityProfile.ProcessDurable, 1);
        await RequiredProcessingPolicyAssertions.ReceiptAsync(Receipt, seeded.Receipt);
        await Assert.That(Harness.ReadCanonicalCut().AppliedPosition).IsEqualTo(next);
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
