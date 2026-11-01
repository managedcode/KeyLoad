using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication;
using P = KeyLoad.FunctionalTests.Authorization.NativePolicyEpochProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class NativePolicyEpochState(NativePolicyEpochOwner owner)
{
    internal NativePolicyEpochOwner Owner
    {
        get;
    } = owner;
    internal ZoneTreeStore Store => Owner.Store;
    internal DatabaseEngine Database => Owner.Database;
    internal PartitionRef Partition
    {
        get;
    } = new("native-policy-tenant", "native-policy-database", "native-policy-domain", P.PartitionKey);
    private readonly TestDatabaseOriginalResourceCommands originalResources = new();
    internal EntityRef Reference => originalResources.Reference(new(Partition, P.Collection, P.Document));
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
    {
        var operation = Database.CreateCommand(kind, id ?? Guid.NewGuid(), principal, default,
            System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options));
        var before = (TestDatabaseOriginalResourceCommands.Begin(Database, operation)).Value;
        var result = Database.ApplyEmbedded(operation, token);
        originalResources.Observe(Database, operation, result, before);
        return result;
    }

    internal byte[] Outcome(CommandRequest command)
        => Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Partition, P.Root, command.CommandId)))
            ?? throw new InvalidOperationException("The original persisted command outcome is absent.");
}
