using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.QueryExecution.BinderFieldLineageProtocol;
using P = KeyLoad.UnitTests.Features.QueryExecution.SchemaVisibleProjectionProtocol;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SchemaVisibleProjectionAuthority
{
    private const string InvalidIdentifier = "An identifier is missing, too long or contains control characters.";

    internal static async Task SetAsync(TestDatabase fixture, long epoch, bool raw, bool schema)
    {
        var grants = new List<string> { RawUse, SqlEventReferenceJoinFixture.RefRead, SqlEventReferenceJoinFixture.RefUse };
        if (raw)
        {
            grants.Add(RawRead);
        }
        if (schema)
        {
            grants.Add(P.SchemaGrant);
        }
        var principal = new PrincipalRecord(P.Reader, Tenant,
            [new(Database, Collection, Capability.DocumentsRead | Capability.Query),
                new(Database, SqlEventReferenceJoinFixture.StreamSet, Capability.EventsRead | Capability.Query),
                new(Database, SqlEventReferenceJoinFixture.Target, Capability.DocumentsRead | Capability.Query)], [.. grants])
        {
            PolicyEpoch = epoch
        };
        await BinderFieldLineagePages.EqualAsync(principal, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>());
        await BinderFieldLineagePages.EqualAsync(principal,
            fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(P.Reader))));
    }

    internal static async Task InvalidAsync(TestDatabase fixture, ResourceDefinition resource)
    {
        var before = fixture.Store.Read(view => view.GetRecord<ResourceDefinition>(KeySpace.Resource(Tenant, Database, Collection)));
        {
            var invalid = resource with
            {
                SchemaVersion = P.MixedVersion,
                FieldPolicies = [resource.FieldPolicies[SchemaVisibleProjectionProtocol.First] with
                {
                    SchemaReadGrant = P.EmptyGrant
                }]
            };
            var id = Guid.NewGuid();
            var request = new ConfigureResourceRequest(Tenant, Database, invalid) { ExpectedSchemaVersion = resource.SchemaVersion };
            var domainBefore = DomainImage(fixture);
            var result = fixture.Submit(OperationKind.ConfigureResource, request, id: id);
            await Assert.That(Errors.Code(result.Problem)).IsEqualTo(ErrorCode.Validation);
            await Assert.That(result.Problem?.Detail).IsEqualTo(InvalidIdentifier);
            await Assert.That(result.Value is null).IsTrue();
            var error = Assert.ThrowsExactly<KeyLoadException>(() => result.Get<ResourceDefinition>());
            await BinderFieldLineagePages.EqualAsync(result, fixture.Submit(OperationKind.ConfigureResource, request, id: id));
            await BinderFieldLineagePages.EqualAsync(domainBefore, DomainImage(fixture));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(error.StatusCode).IsEqualTo(Errors.Status(ErrorCode.Validation));
            await Assert.That(error.InnerException is null).IsTrue();
            await Assert.That(error.Message).IsEqualTo(InvalidIdentifier);
        }
        await BinderFieldLineagePages.EqualAsync(before, fixture.Store.Read(view =>
            view.GetRecord<ResourceDefinition>(KeySpace.Resource(Tenant, Database, Collection))));
    }

    internal static ResourceDefinition Mixed(ResourceDefinition resource) => resource with
    {
        SchemaVersion = P.MixedVersion,
        FieldPolicies = [resource.FieldPolicies[P.First] with { SchemaReadGrant = null }, resource.FieldPolicies[SecondPolicy]]
    };

    private static string[] DomainImage(TestDatabase fixture) => fixture.Store.Read(view =>
    {
        var rows = new List<string>();
        foreach (var family in new[] { "document", "index", "unique", "document-epoch", "outbox", "outbox-head" })
        {
            var page = view.Scan(KeySpace.Partition(family, fixture.Partition), fixture.Database.Limits.MaxScanRecords);
            if (page.HasMore)
            {
                throw new InvalidOperationException("The schema refusal domain image exceeded its configured bound.");
            }
            rows.AddRange(page.Records.Select(row => Convert.ToHexString(row.Key.Span) + ":" + Convert.ToHexString(row.Value.Span)));
        }
        return rows.ToArray();
    });

    private const int SecondPolicy = 1;
}
