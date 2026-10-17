using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeProtocol;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeSupport;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentAuthorityIntersectionNativeFields
{
    internal static async Task RunAsync(RemoteDocumentNativeFixture fixture)
    {
        fixture.Seed();
        await ResourceAsync(fixture.Source, new SensitiveFieldPolicy(SecretPath, Classification, RawReadGrant: SourceRead));
        await ResourceAsync(fixture.Destination, new SensitiveFieldPolicy(SecretPath, Classification, RawReadGrant: TargetRead));
        var source = Principal(SourceEpoch, []);
        var target = Principal(TargetEpoch, [SourceRead, TargetRead]);
        await PrincipalAsync(fixture.Source, source);
        await PrincipalAsync(fixture.Destination, target);
        var write = await WriteAsync(fixture, new PutDocument(RemoteDocumentNativeFixture.Collection, FieldId, FieldJson));
        await EqualAsync(new DocumentResult(Reference(FieldId), Revision, FieldJson, false, []),
            fixture.Destination.Database.GetDocument(RemoteDocumentNativeFixture.Reader, Reference(FieldId)));
        await RequireDocumentAsync(fixture, FieldId, FieldSafe, true, [SecretPath]);
        source = source with
        {
            PolicyEpoch = source.PolicyEpoch + EpochStep,
            FieldGrants = [SourceRead, TargetRead]
        };
        await PrincipalAsync(fixture.Source, source);
        await RequireDocumentAsync(fixture, FieldId, FieldJson, false, []);
        target = target with
        {
            PolicyEpoch = target.PolicyEpoch + EpochStep,
            FieldGrants = [SourceRead]
        };
        await PrincipalAsync(fixture.Destination, target);
        await RequireDocumentAsync(fixture, FieldId, FieldSafe, true, [SecretPath]);
        await EqualAsync(new DocumentResult(Reference(FieldId), Revision, FieldSafe, true, [SecretPath]),
            fixture.Destination.Database.GetDocument(RemoteDocumentNativeFixture.Reader, Reference(FieldId)));
        target = target with
        {
            PolicyEpoch = target.PolicyEpoch + EpochStep,
            FieldGrants = [SourceRead, TargetRead]
        };
        await PrincipalAsync(fixture.Destination, target);
        await Assert.That(source.PolicyEpoch).IsNotEqualTo(target.PolicyEpoch);
        await RequireDocumentAsync(fixture, FieldId, FieldJson, false, []);
        var before = RemoteDocumentNativeAssertions.Image(fixture.Destination);
        await EqualAsync(write.Receipt, fixture.Destination.Database.Apply(write.Operation).Get<CommitReceipt>());
        await Assert.That(RemoteDocumentNativeAssertions.Image(fixture.Destination).SequenceEqual(before)).IsTrue();
        fixture.Source.Reopen();
        fixture.Destination.Reopen();
        await RequireDocumentAsync(fixture, FieldId, FieldJson, false, []);
        fixture.Source.Reopen();
        fixture.Destination.Reopen();
        await RequireDocumentAsync(fixture, FieldId, FieldJson, false, []);
    }
}
