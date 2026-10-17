using System.Collections.Immutable;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Storage;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeProtocol;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeSupport;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentAuthorityIntersectionNativeProjection
{
    private const string ShapesId = "authority-shapes";
    private const string ShapesJson = "{\"public\":\"ok\",\"rows\":[{\"secret\":\"a\",\"open\":1},{\"secret\":\"b\",\"open\":2}],\"array\":[10,20,30],\"nested\":{\"secret\":\"c\",\"open\":3}}";
    private const string ShapesSafe = "{\"public\":\"ok\",\"rows\":[{\"open\":1},{\"open\":2}],\"array\":[10,null,30],\"nested\":{\"open\":3}}";
    private const string WildcardPath = "/rows/*/secret";
    private const string ArrayPath = "/array/1";
    private const string NestedPath = "/nested/secret";
    private const string RootPath = "";
    private const string EmptyJson = "{}";

    internal static async Task RunAsync(RemoteDocumentNativeFixture fixture)
    {
        fixture.Seed();
        var source = Principal(SourceEpoch, []);
        var target = Principal(TargetEpoch, []);
        await PrincipalAsync(fixture.Source, source);
        await PrincipalAsync(fixture.Destination, target);
        await ResourceAsync(fixture.Source, new SensitiveFieldPolicy(WildcardPath, Classification, RawReadGrant: SourceRead));
        await ResourceAsync(fixture.Destination, new SensitiveFieldPolicy(ArrayPath, Classification, RawReadGrant: TargetRead),
            new SensitiveFieldPolicy(NestedPath, Classification, RawReadGrant: TargetRead));
        _ = await WriteAsync(fixture, new PutDocument(RemoteDocumentNativeFixture.Collection, ShapesId, ShapesJson),
            new PutDocument(RemoteDocumentNativeFixture.Collection, FieldId, FieldJson));
        await RequireDocumentAsync(fixture, ShapesId, ShapesSafe, true, [ArrayPath, NestedPath, WildcardPath]);
        await ProjectionAsync(fixture, source, target, ShapesId, ShapesSafe,
            [ArrayPath, NestedPath, WildcardPath], [ArrayPath, NestedPath, WildcardPath]);
        (source, target) = await SchemaAsync(fixture, source, target);
        await RootAsync(fixture, source, target);
    }

    private static async Task<(PrincipalRecord Source, PrincipalRecord Target)> SchemaAsync(RemoteDocumentNativeFixture fixture, PrincipalRecord source, PrincipalRecord target)
    {
        await ResourceAsync(fixture.Source, new SensitiveFieldPolicy(SecretPath, Classification,
            RawReadGrant: SourceRead, SchemaReadGrant: SourceSchema));
        await ResourceAsync(fixture.Destination, new SensitiveFieldPolicy(SecretPath, Classification,
            RawReadGrant: TargetRead, SchemaReadGrant: TargetSchema));
        await RequireDocumentAsync(fixture, FieldId, FieldSafe, true, []);
        source = source with
        {
            PolicyEpoch = source.PolicyEpoch + EpochStep,
            FieldGrants = [SourceSchema]
        };
        await PrincipalAsync(fixture.Source, source);
        await RequireDocumentAsync(fixture, FieldId, FieldSafe, true, []);
        target = target with
        {
            PolicyEpoch = target.PolicyEpoch + EpochStep,
            FieldGrants = [TargetSchema]
        };
        await PrincipalAsync(fixture.Destination, target);
        await RequireDocumentAsync(fixture, FieldId, FieldSafe, true, [SecretPath]);
        await ProjectionAsync(fixture, source, target, FieldId, FieldSafe, [SecretPath], [SecretPath]);
        source = source with
        {
            PolicyEpoch = source.PolicyEpoch + EpochStep,
            FieldGrants = [TargetSchema]
        };
        await PrincipalAsync(fixture.Source, source);
        await RequireDocumentAsync(fixture, FieldId, FieldSafe, true, []);
        await ProjectionAsync(fixture, source, target, FieldId, FieldSafe, [SecretPath], []);
        return (source, target);
    }

    private static async Task RootAsync(RemoteDocumentNativeFixture fixture, PrincipalRecord originalSource,
        PrincipalRecord originalTarget)
    {
        var source = originalSource with
        {
            PolicyEpoch = originalSource.PolicyEpoch + EpochStep,
            FieldGrants = []
        };
        var target = originalTarget with
        {
            PolicyEpoch = originalTarget.PolicyEpoch + EpochStep,
            FieldGrants = []
        };
        await PrincipalAsync(fixture.Source, source);
        await PrincipalAsync(fixture.Destination, target);
        await ResourceAsync(fixture.Source, new SensitiveFieldPolicy(RootPath, Classification, RawReadGrant: SourceRead, SchemaReadGrant: SourceSchema));
        await ResourceAsync(fixture.Destination);
        await RequireDocumentAsync(fixture, ShapesId, EmptyJson, true, []);
        await ProjectionAsync(fixture, source, target, ShapesId, EmptyJson, [RootPath], []);
        await ResourceAsync(fixture.Source);
        await ResourceAsync(fixture.Destination, new SensitiveFieldPolicy(RootPath, Classification, RawReadGrant: TargetRead, SchemaReadGrant: TargetSchema));
        await RequireDocumentAsync(fixture, ShapesId, EmptyJson, true, []);
        await ProjectionAsync(fixture, source, target, ShapesId, EmptyJson, [RootPath], []);
        fixture.Source.Reopen();
        fixture.Destination.Reopen();
        await RequireDocumentAsync(fixture, ShapesId, EmptyJson, true, []);
    }

    private static async Task ProjectionAsync(RemoteDocumentNativeFixture fixture, PrincipalRecord source,
        PrincipalRecord target, string id, string expectedJson, ImmutableArray<string> complete,
        ImmutableArray<string> visible)
    {
        var reference = Reference(id);
        var record = fixture.Destination.Store.Read(view => view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(reference)))
            ?? throw new InvalidOperationException(Missing);
        var actual = fixture.Destination.Database.ProjectWithAuthorityIntersection(source, Resource(fixture.Source),
            target, Resource(fixture.Destination), record);
        await EqualAsync(new DocumentResult(reference, Revision, expectedJson, true, visible), actual.Value);
        await EqualAsync(complete, actual.CompleteOmittedPaths);
    }
}
