using ManagedCode.Communication;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using P = KeyLoad.UnitTests.Features.ResourceExecution.CachePolicyEpochProtocol;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CachePolicyEpochState(CachePolicyEpochOwner owner)
{
    internal CachePolicyEpochOwner Owner
    {
        get;
    } = owner;
    internal ZoneTreeStore Store => Owner.Store;
    internal DatabaseEngine Database => Owner.Database;
    internal PartitionRef Partition
    {
        get;
    } = new("cache-policy-tenant", "cache-policy-database", "cache-policy-domain", P.PartitionKey);
    internal EntityRef Reference => new(Partition, P.Collection, P.Document);
    internal ResourceDefinition Resource
    {
        get;
        set;
    } = null!;
    internal PrincipalRecord Reader
    {
        get;
        set;
    } = null!;
    internal CommandRequest Seed
    {
        get;
        set;
    } = null!;
    internal CommitReceipt SeedReceipt
    {
        get;
        set;
    } = null!;
    internal CommandRequest Healthy
    {
        get;
        set;
    } = null!;
    internal CommitReceipt HealthyReceipt
    {
        get;
        set;
    } = null!;
    internal byte[] SeedOutcome
    {
        get;
        set;
    } = [];
    internal byte[] HealthyOutcome
    {
        get;
        set;
    } = [];

    internal Result<object> Submit<T>(OperationKind kind, T payload, CancellationToken token,
        Guid? id = null, string principal = P.Root)
        => Database.ApplyEmbedded(Database.CreateCommand(kind, id ?? Guid.NewGuid(), principal, default, System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options)), token);

    internal byte[] Outcome(CommandRequest command)
        => Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Partition, P.Root, command.CommandId)))
            ?? throw new InvalidOperationException("The original persisted command outcome is absent.");
}
