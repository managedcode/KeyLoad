using KeyLoad.Server;
using A = KeyLoad.UnitTests.Features.Search.TextCapturedCommitAuthorityAssertions;
using P = KeyLoad.UnitTests.Features.Search.TextCapturedCommitAuthorityProtocol;

namespace KeyLoad.UnitTests.Features.Search;

internal static class TextCapturedCommitAuthorityFlow
{
    private const int MaximumDiagnosticCharacters = 4096;

    internal static async Task RunAsync(CancellationToken token)
    {
        TestDatabase? fixture = null;
        TextCapturedCommitAuthorityHarness? original = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            fixture.Configure(P.Collection, ResourceKind.Collection);
            var principal = P.Principal(fixture.Partition);
            await Assert.That(principal.ClusterAdministrator).IsFalse();
            var persisted = fixture.Submit(OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
            var actualPrincipal = System.Text.Json.JsonSerializer.Serialize(persisted, JsonDefaults.Options);
            var expectedPrincipal = System.Text.Json.JsonSerializer.Serialize(principal, JsonDefaults.Options);
            await Assert.That(StringComparer.Ordinal.Equals(actualPrincipal, expectedPrincipal)).IsTrue()
                .Because($"actual={Bounded(actualPrincipal)} expected={Bounded(expectedPrincipal)}");
            var seed = P.Seed(fixture.Partition, Guid.NewGuid());
            var seeded = fixture.Submit(OperationKind.Batch, seed, id: seed.CommandId).Get<CommitReceipt>();
            await A.SeedReceiptAsync(fixture.Partition, seed, seeded);
            // Harness uses the actual unconfigured engine over this same execution canonical store.
            original = new(fixture, P.Subject, P.Request(fixture.Partition), token);
            await ExecuteAsync(fixture, original, token);
        }, failures);
        if (original is not null)
        {
            ServerFailureObserver.Observe(original.Dispose, failures);
        }
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(TestDatabase fixture, TextCapturedCommitAuthorityHarness original,
        CancellationToken token)
    {
        var identity = fixture.Store.Identity;
        var cut = original.CapturedPosition;
        await Assert.That(identity.ReadGeneration).IsEqualTo(original.CapturedReadGeneration);
        var command = P.Mutation(fixture.Partition, Guid.NewGuid());
        var committed = fixture.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await A.ReceiptAsync(fixture.Partition, command, committed);
        await A.CutsAsync(fixture, identity, cut);
        // No current Resource/Get/WithQueryView/Search read may rewarm the cache before Complete.
        var old = original.Complete();
        await A.OldAsync(fixture, old);
        var fresh = await original.Search.SearchAsync(P.Subject, P.Request(fixture.Partition), token);
        await A.FreshAsync(fixture, fresh);
        await A.JournalAsync(fixture, command, committed);
        var beforeReplay = fixture.Store.Position;
        var replay = fixture.Submit(OperationKind.Batch, command, id: command.CommandId).Get<CommitReceipt>();
        await A.ReplayAsync(committed, replay);
        await Assert.That(fixture.Store.Position).IsEqualTo(beforeReplay);
        await A.CutsAsync(fixture, identity, cut);
        await A.JournalAsync(fixture, command, replay);
        await A.FreshAsync(fixture,
            await original.Search.SearchAsync(P.Subject, P.Request(fixture.Partition), token));
    }

    private static string Bounded(string value) => value[..Math.Min(value.Length, MaximumDiagnosticCharacters)];
}
