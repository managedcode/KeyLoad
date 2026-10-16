using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeProtocol;
using static KeyLoad.UnitTests.Features.DocumentStorage.DocumentAuthorityIntersectionNativeSupport;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal static class DocumentAuthorityIntersectionNativeRows
{
    private const string OwnerA = "source-owner";
    private const string OwnerB = "target-owner";
    private const string Project = "source-project";
    private const string Team = "target-team";
    private const string BothOwnerTeam = "both-owner-team";
    private const string BothProjectOwner = "both-project-owner";
    private const string BothProjectTeam = "both-project-team";
    private const string SourceOnly = "source-only";
    private const string TargetOnly = "target-only";
    private const string Neither = "neither";

    internal static async Task RunAsync(RemoteDocumentNativeFixture fixture)
    {
        fixture.Seed();
        await ResourceAsync(fixture.Source);
        await ResourceAsync(fixture.Destination);
        var source = Principal(SourceEpoch, []) with
        {
            RestrictRows = true,
            OwnerId = OwnerA,
            Projects = [Project]
        };
        var target = Principal(TargetEpoch, []) with
        {
            RestrictRows = true,
            OwnerId = OwnerB,
            Teams = [Team]
        };
        await PrincipalAsync(fixture.Source, source);
        await PrincipalAsync(fixture.Destination, target);
        var write = await WriteAsync(fixture,
            Row(BothOwnerTeam, new(OwnerA, TeamId: Team)), Row(BothProjectOwner, new(OwnerB, Project)),
            Row(BothProjectTeam, new(ProjectId: Project, TeamId: Team)), Row(SourceOnly, new(OwnerA)),
            Row(TargetOnly, new(OwnerB)), Row(Neither, new()));
        foreach (var id in new[] { BothOwnerTeam, BothProjectOwner, BothProjectTeam })
        {
            await RequireDocumentAsync(fixture, id, RowJson, false, []);
        }
        foreach (var id in new[] { SourceOnly, TargetOnly, Neither })
        {
            await Assert.That((await ReadAsync(fixture, id)).Document).IsNull();
        }
        var targetOnly = fixture.Destination.Database.GetDocument(RemoteDocumentNativeFixture.Reader, Reference(TargetOnly));
        await EqualAsync(new DocumentResult(Reference(TargetOnly), Revision, RowJson, false, []), targetOnly);
        await Assert.That(fixture.Destination.Database.GetDocument(RemoteDocumentNativeFixture.Reader,
            Reference(SourceOnly))).IsNull();
        source = source with
        {
            PolicyEpoch = source.PolicyEpoch + EpochStep,
            RestrictRows = false
        };
        await PrincipalAsync(fixture.Source, source);
        await RequireDocumentAsync(fixture, TargetOnly, RowJson, false, []);
        await Assert.That((await ReadAsync(fixture, SourceOnly)).Document).IsNull();
        target = target with
        {
            PolicyEpoch = target.PolicyEpoch + EpochStep,
            RestrictRows = false
        };
        await PrincipalAsync(fixture.Destination, target);
        await Assert.That(source.PolicyEpoch).IsNotEqualTo(target.PolicyEpoch);
        foreach (var id in new[] { SourceOnly, TargetOnly, Neither })
        {
            await RequireDocumentAsync(fixture, id, RowJson, false, []);
        }
        var position = fixture.Destination.Store.Position;
        await EqualAsync(write.Receipt, fixture.Destination.Database.Apply(write.Operation).Get<CommitReceipt>());
        await Assert.That(fixture.Destination.Store.Position).IsEqualTo(position);
        fixture.Source.Reopen();
        fixture.Destination.Reopen();
        await RequireDocumentAsync(fixture, BothProjectTeam, RowJson, false, []);
        await RequireDocumentAsync(fixture, Neither, RowJson, false, []);
    }

    private static PutDocument Row(string id, RowAccess access)
        => new(RemoteDocumentNativeFixture.Collection, id, RowJson, Access: access);
}
