using System.Collections.Immutable;
using KeyLoad.UnitTests.Authorization;

namespace KeyLoad.UnitTests.TimeSeries;

internal sealed class TimeSeriesChunkQualificationFixture : IDisposable
{
    internal TestDatabase Harness
    {
        get;
    } = new();
    internal Guid Window
    {
        get;
    } = Guid.NewGuid();
    internal PartitionRef[] Partitions
    {
        get;
        private set;
    } = [];
    internal List<(CommandRequest Command, CommitReceipt Receipt, byte[] Outcome)> Originals
    {
        get;
    } = [];
    internal string[] SiblingImage
    {
        get;
        private set;
    } = [];
    internal int Phase
    {
        get;
        private set;
    }
    internal long Purged
    {
        get;
        private set;
    }
    internal bool HasMore
    {
        get;
        private set;
    }
    internal async Task InitializeAsync()
    {
        var first = Harness.Partition;
        var second = first with
        {
            PartitionKey = Guid.NewGuid().ToString(TimeSeriesChunkQualificationData.GuidFormat)
        };
        Partitions = [first, second];
        await Assert.That(second.AtomicPartitionId).IsNotEqualTo(first.AtomicPartitionId);
        var definition = new ResourceDefinition(TimeSeriesChunkQualificationData.Set, ResourceKind.TimeSeries, first.TransactionDomainId)
        {
            FieldPolicies = [new(TimeSeriesChunkQualificationData.PrivatePath, TimeSeriesChunkQualificationData.RestrictedClass, TimeSeriesChunkQualificationData.ReadGrant, TimeSeriesChunkQualificationData.UseGrant)]
        };
        await RequiredProcessingPolicyAssertions.EqualAsync(definition, Harness.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(first.TenantId, first.DatabaseId, definition)).Get<ResourceDefinition>());
        for (var index = 0; index < Partitions.Length; index++)
        {
            await CommitAsync(TimeSeriesChunkQualificationData.Seed(Partitions[index], Window, index == TimeSeriesChunkQualificationData.SiblingScope),
                TimeSeriesChunkQualificationData.SeedReceipts);
        }
        SiblingImage = TimeSeriesChunkQualificationScopeImage.Capture(Harness.Store, Partitions[TimeSeriesChunkQualificationData.SiblingScope]);
        await PolicyAsync(TimeSeriesChunkQualificationData.InitialPolicyEpoch, true, false);
    }
    internal async Task CommitAsync(CommandRequest command, ImmutableArray<MutationReceipt> mutations,
        int? phase = null, long? purged = null, bool more = false)
    {
        var db = Harness;
        var expected = new CommitReceipt(command.CommandId, new(db.Store.Identity.Incarnation,
            command.Partition.AtomicPartitionId, db.ReadCanonicalCut().AppliedPosition + TimeSeriesChunkQualificationData.NextJournalIndex), mutations, DurabilityProfile.ProcessDurable, 1);
        var actual = db.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        Originals.Add((command, expected, RequiredProcessingPolicyAssertions.Outcome(db.Store, command.Partition, TimeSeriesChunkQualificationData.RootPrincipal, command.CommandId)));
        Phase = phase ?? Phase;
        Purged = purged ?? Purged;
        HasMore = more;
        await RequiredProcessingPolicyAssertions.ReceiptAsync(expected, actual);
    }
    internal async Task PolicyAsync(long epoch, bool allow, bool use)
    {
        var expected = new PrincipalRecord(TimeSeriesChunkQualificationData.Reader, Harness.Partition.TenantId,
            allow ? [new(Harness.Partition.DatabaseId, TimeSeriesChunkQualificationData.Set, Capability.SeriesRead)] : [],
            use ? [TimeSeriesChunkQualificationData.UseGrant] : [])
        {
            PolicyEpoch = epoch
        };
        await RequiredProcessingPolicyAssertions.EqualAsync(expected, Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>());
    }
    public void Dispose() => Harness.Dispose();
}
