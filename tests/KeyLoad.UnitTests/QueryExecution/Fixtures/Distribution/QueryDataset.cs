namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Seeds canonical query data and persisted policies in independent real stores.</summary>
internal sealed class QueryDataset(QueryFixture fixture)
{
    internal const string SourceJson = "{\"title\":\"source\",\"secret\":\"source-private-canary\"}";
    internal const string SourceProjected = "{\"title\":\"source\"}";
    internal const long SourceEpoch = 3;
    internal const long DestinationEpoch = 2;
    internal static PartitionRef Local => QueryFixture.Partition with { PartitionKey = "source" };

    internal void Seed()
    {
        fixture.Seed();
        SetSourceGrant(DestinationEpoch, true);
        SetSourceGrant(SourceEpoch, true);
        var id = Guid.NewGuid();
        _ = fixture.Source.Submit(QueryFixture.Operation(fixture.Source, OperationKind.Batch, id,
            new CommandRequest(id, Local, [new PutDocument(QueryFixture.Collection, QueryFixture.DocumentId,
                SourceJson)]))).Get<CommitReceipt>();
    }

    internal void GrantDestination() => QueryFixture.Principal(fixture.Destination,
        Principal(DestinationEpoch, true));

    internal void SetSourceGrant(long epoch, bool granted) => QueryFixture.Principal(fixture.Source,
        Principal(epoch, granted));

    private static PrincipalRecord Principal(long epoch, bool granted) => new(QueryFixture.Reader,
        QueryFixture.Tenant, granted ? [new(Local.DatabaseId, QueryFixture.Collection,
            Capability.Query | Capability.DocumentsRead)] : [], [])
    { PolicyEpoch = epoch };

}
