namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Seeds canonical query data and persisted policies in independent real stores.</summary>
internal sealed class QueryNativeDataset(QueryNativeFixture fixture)
{
    internal const string SourceJson = "{\"title\":\"source\",\"secret\":\"source-private-canary\"}";
    internal const string SourceProjected = "{\"title\":\"source\"}";
    internal const long SourceEpoch = 3;
    internal const long DestinationEpoch = 2;
    internal static PartitionRef Local => QueryNativeFixture.Partition with { PartitionKey = "source" };

    internal void Seed()
    {
        fixture.Seed();
        SetSourceGrant(DestinationEpoch, true);
        SetSourceGrant(SourceEpoch, true);
        var id = Guid.NewGuid();
        _ = fixture.Source.Submit(QueryNativeFixture.Operation(fixture.Source, OperationKind.Batch, id,
            new CommandRequest(id, Local, [new PutDocument(QueryNativeFixture.Collection, QueryNativeFixture.DocumentId,
                SourceJson)]))).Get<CommitReceipt>();
    }

    internal void GrantDestination() => QueryNativeFixture.Principal(fixture.Destination,
        Principal(DestinationEpoch, true));

    internal void SetSourceGrant(long epoch, bool granted) => QueryNativeFixture.Principal(fixture.Source,
        Principal(epoch, granted));

    private static PrincipalRecord Principal(long epoch, bool granted) => new(QueryNativeFixture.Reader,
        QueryNativeFixture.Tenant, granted ? [new(Local.DatabaseId, QueryNativeFixture.Collection,
            Capability.Query | Capability.DocumentsRead)] : [], []) { PolicyEpoch = epoch };

}
