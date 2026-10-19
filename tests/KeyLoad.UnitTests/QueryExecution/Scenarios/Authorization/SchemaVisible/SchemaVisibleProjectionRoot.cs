using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Storage.ZoneTree;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;
using P = KeyLoad.UnitTests.Features.QueryExecution.SchemaVisibleProjectionProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionRoot
{
    private const long RootVersion = 3;
    private const long RootEpoch = 6;
    private const string RootPath = "";
    private const string EmptyJson = "{}";
    private const string Equal = "=";
    private const string Id = "/@id";
    private const string Revision = "/@revision";
    private const string PayloadId = "/id";
    private const string PayloadRevision = "/revision";
    private const string SqlSystem = "SELECT d.id AS exposed FROM orders d WHERE d.id = 'o1' ORDER BY d.revision";
    private const string Point = "point";

    internal static async Task ConfigureAsync(TestDatabase fixture, ResourceDefinition previous)
    {
        var resource = previous with
        {
            SchemaVersion = RootVersion,
            FieldPolicies = [new(RootPath, Classification, RawReadGrant: RawRead, RawUseGrant: RawUse, SchemaReadGrant: P.SchemaGrant),
                new(StatusPath, Classification, RawReadGrant: RawRead, RawUseGrant: RawUse)]
        };
        await BinderFieldLineagePages.EqualAsync(resource, fixture.Submit(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(Tenant, Database, resource) { ExpectedSchemaVersion = previous.SchemaVersion }).Get<ResourceDefinition>());
        var principal = new PrincipalRecord(P.Reader, Tenant,
            [new(Database, Collection, Capability.DocumentsRead | Capability.Query)], [])
        {
            PolicyEpoch = RootEpoch
        };
        await BinderFieldLineagePages.EqualAsync(principal, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
    }

    internal static async Task WaveAsync(DatabaseEngine database, QueryEngine engine, ZoneTreeStore store, PartitionRef partition, CancellationToken token)
    {
        var star = SchemaVisibleProjectionRequests.Star(partition);
        var system = star with
        {
            Query = star.Query with
            {
                Projection = [new(Id, P.ValueAlias)],
                Filter = new Comparison(new FieldOperand(Id), Equal, ValueOperand.Create(DocumentId)),
                Order = [new(Revision, false)]
            }
        };
        await SchemaVisibleProjectionAssertions.ObserveAsync(store, async () =>
        {
            await BinderFieldLineagePages.EqualAsync<DocumentResult?>(new(new(partition, Collection, DocumentId),
                OriginalRevision, EmptyJson, true, []), database.GetDocument(P.Reader, new(partition, Collection, DocumentId), cancellationToken: token));
            var expected = new QueryPage([new(DocumentId, OriginalRevision, P.IdJson, false, [])], null, store.Position, Point);
            await BinderFieldLineagePages.EqualAsync(expected, engine.Execute(P.Reader, new(partition, SqlSystem, AllowFullScan: true), cancellationToken: token));
            await BinderFieldLineagePages.EqualAsync(expected, engine.ExecuteAst(P.Reader, system, cancellationToken: token));
            var revision = system with
            {
                Query = system.Query with
                {
                    Projection = [new(Revision, P.ValueAlias)]
                }
            };
            await BinderFieldLineagePages.EqualAsync(new QueryPage([new(DocumentId, OriginalRevision, P.RevisionJson, false, [])], null,
                store.Position, Point), engine.ExecuteAst(P.Reader, revision, cancellationToken: token));
            await BinderFieldLineagePages.EqualAsync(new QueryPage([new(DocumentId, OriginalRevision, EmptyJson, true, [])],
                null, store.Position, P.Scan), engine.ExecuteAst(P.Reader, star, cancellationToken: token));
        });
        foreach (var path in new[] { PayloadId, PayloadRevision })
        {
            var ordinary = star with
            {
                Query = star.Query with
                {
                    Projection = [new(path, P.ValueAlias)]
                }
            };
            await SchemaVisibleProjectionAssertions.ObserveAsync(store, () =>
                SchemaVisibleProjectionAssertions.RefusalAsync(() => engine.ExecuteAst(P.Reader, ordinary, cancellationToken: token)));
        }
        var explain = star with
        {
            Query = star.Query with
            {
                Explain = true
            }
        };
        await SchemaVisibleProjectionAssertions.ObserveAsync(store, () =>
            SchemaVisibleProjectionAssertions.RefusalAsync(() => engine.ExecuteAst(P.Reader, explain, cancellationToken: token)));
    }
}
