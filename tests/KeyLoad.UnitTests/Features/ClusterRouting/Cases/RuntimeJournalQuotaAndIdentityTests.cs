using KeyLoad.Core;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RuntimeJournalQuotaAndIdentityTests
{
    private const string FirstName = "quota-a";
    private const string SecondName = "quota-b";
    private const string FirstInstance = "33445566-7788-99aa-bbcc-ddeeff001122";
    private const string SecondInstance = "44556677-8899-aabb-ccdd-eeff00112233";
    private const string BootstrapJournal = "bootstrap-idempotency";

    [Test]
    public async Task CapacityRejectionLeavesBodyAndCatalogUnchanged()
    {
        using var fixture = new RuntimeJournalFixture(new RuntimeJournalOptions
        { MaximumJournals = 1, MaximumJournalBytes = 8, MaximumTotalBytes = 8, ChunkBytes = 4 });
        var header = fixture.Create(FirstName, FirstInstance);
        var filled = fixture.Submit(RuntimeJournalFixture.Mutation(RuntimeJournalAction.Append, header, [1, 2, 3, 4])).Snapshot!;
        var rejected = RuntimeJournalFixture.Mutation(RuntimeJournalAction.Append, filled, [5, 6, 7, 8, 9]);
        var exhausted = await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.Submit(rejected)));
        await Assert.That(exhausted!.Code).IsEqualTo(ErrorCode.ResourceExhausted);

        var current = fixture.Engine.GetRuntimeJournalHeader(RuntimeJournalFixture.JournalPrincipal, FirstName)!;
        var page = fixture.Engine.ReadRuntimeJournal(RuntimeJournalFixture.JournalPrincipal,
            new(FirstName, current.InstanceId, current.OwnerGeneration, current.ContentRevision, 0));
        await Assert.That(page.Data.ToArray()).IsEquivalentTo(new byte[] { 1, 2, 3, 4 }, CollectionOrdering.Matching);
        await Assert.That(current.ContentRevision).IsEqualTo(filled.ContentRevision);
        var deniedCreate = new RuntimeJournalMutation(RuntimeJournalAction.Create, SecondName, Guid.Parse(SecondInstance),
            0, 0, null, ReadOnlyMemory<byte>.Empty, new(StringComparer.Ordinal), []);
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.Submit(deniedCreate)));
        await Assert.That(fixture.Engine.ReadRuntimeJournalCatalog(RuntimeJournalFixture.JournalPrincipal).Journals.Length)
            .IsEqualTo(1);
    }

    [Test]
    public async Task BootstrapIsIdempotentAndPublicCredentialPathsRejectPrivateIdentity()
    {
        using var fixture = new RuntimeJournalFixture();
        await Assert.That(fixture.Bootstrap().Applied).IsFalse();
        _ = fixture.Create(BootstrapJournal, FirstInstance);
        await Assert.That(fixture.Bootstrap().Applied).IsFalse();
        var principal = new PrincipalRecord(RuntimeJournalFixture.JournalPrincipal, "system", [], []);
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.SubmitOperation(
            OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>()));
        var credential = DatabaseEngine.Credential("runtime-journal-public", RuntimeJournalFixture.JournalPrincipal,
            "runtime-journal.unit-test-credential-32-characters");
        await Assert.ThrowsExactlyAsync<KeyLoadException>(() => Task.Run(() => fixture.SubmitOperation(
            OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(credential)).Get<ApiKeyRecord>()));
        await Assert.That(fixture.Engine.Store.Read(view => view.GetRecord<PrincipalRecord>(KeySpace.Principal(
            RuntimeJournalFixture.JournalPrincipal))?.ClusterAdministrator)).IsFalse();
    }
}
