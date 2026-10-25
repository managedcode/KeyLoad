using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal static class TenantAdmissionOperations
{
    internal const string Collection = "fair-documents";
    internal const string PrincipalA = "fair-a";
    internal const string PrincipalB = "fair-b";
    internal const string OtherTenant = "other-tenant";
    internal const string Body = "{\"fair\":true}";

    internal static PartitionRef PartitionB(TestDatabase fixture)
        => fixture.Partition with
        {
            TenantId = OtherTenant
        };

    internal static void Provision(TestDatabase fixture)
    {
        fixture.Configure(Collection, ResourceKind.Collection);
        fixture.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(OtherTenant,
            fixture.Partition.DatabaseId, new(Collection, ResourceKind.Collection,
                fixture.Partition.TransactionDomainId))).Get<ResourceDefinition>();
        Configure(fixture, PrincipalA, fixture.Partition.TenantId);
        Configure(fixture, PrincipalB, OtherTenant);
    }

    private static void Configure(TestDatabase fixture, string principal, string tenant)
        => fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(
            new(principal, tenant, [new(fixture.Partition.DatabaseId, Collection,
                Capability.DocumentsRead | Capability.DocumentsWrite)], []))).Get<PrincipalRecord>();

    internal static PrincipalRecord Principal(TestDatabase fixture, string id)
        => fixture.Store.Read(view => fixture.Database.Principal(view, id,
            fixture.Database.EvaluationClock.GetUtcNow()));

    internal static Command<DatabaseCommand> Create(TestDatabase fixture, string principal, string id)
    {
        var commandId = Guid.NewGuid();
        var partition = principal == PrincipalA ? fixture.Partition : PartitionB(fixture);
        var request = new CommandRequest(commandId, partition, [new PutDocument(Collection, id, Body)]);
        return fixture.Database.CreateCommand(OperationKind.Batch, commandId, principal,
            fixture.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(request));
    }
}
