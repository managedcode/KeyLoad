using System.Text.Json;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class PhysicalOwnerRegistrationPolicyReplayFlow
{
    internal const string PrincipalId = "owner-registration-policy-admin";
    private const long OriginalPolicy = 1;
    private const long RevokedPolicy = 2;
    private const long RegrantedPolicy = 3;
    private static readonly Guid OriginalId = Guid.Parse("c4269c4a-2908-4a6d-aa56-c8077b01d8f9");
    private static readonly Guid DeniedId = Guid.Parse("4abf2111-5b9e-4ef4-8ec7-f7993904e759");
    private static readonly Guid FreshId = Guid.Parse("d15d2e1c-9df0-42a9-8f77-9bd37f4cc5f1");
    private static readonly Guid ContinuationId = Guid.Parse("2db7a877-5177-4c6c-9b20-2ff8c09e4092");

    internal static async Task RunAsync()
    {
        var failures = new List<Exception>();
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var fixture = new PhysicalShardCatalogFixture();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(() => ExecuteAsync(fixture), failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task ExecuteAsync(PhysicalShardCatalogFixture fixture)
    {
        PhysicalOwnerDirectoryWholeFlow.Bootstrap(fixture);
        await SetAsync(fixture, true, OriginalPolicy);
        var original = Operation(fixture, OriginalId, PhysicalOwnerDirectoryWholeFlow.EmptyRevision);
        var result = fixture.Database.Apply(original);
        await Assert.That(result.Error).IsNull();
        var outcome = PhysicalOwnerRegistrationPolicyReplayAssertions.Outcome(fixture, OriginalId);
        var directory = PhysicalOwnerDirectoryWholeFlow.DirectoryBytes(fixture);
        var catalog = fixture.ReadCatalogBytes();
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, original, result, outcome, OriginalPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AuthorityAsync(fixture, directory, catalog);
        var denied = await RevokeAndColdAsync(fixture, original, outcome, directory, catalog);
        await RegrantAndColdAsync(fixture, original, result, outcome, denied, directory, catalog);
    }

    private static async Task<DeniedReceipt> RevokeAndColdAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, byte[] outcome, byte[] directory, byte[] catalog)
    {
        await SetAsync(fixture, false, RevokedPolicy);
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var cut = fixture.Store.Position;
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AdministrationReplayAsync(fixture, original, outcome);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PersistedAsync(fixture, false, RevokedPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AdministrationReplayAsync(fixture, original, outcome);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AuthorityAsync(fixture, directory, catalog);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        var before = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var denied = Operation(fixture, DeniedId, PhysicalOwnerDirectoryWholeFlow.InitialRevision);
        var result = fixture.Database.Apply(denied);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AdministrationDeniedAsync(result);
        var bytes = PhysicalOwnerRegistrationPolicyReplayAssertions.Outcome(fixture, DeniedId);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, denied, result, bytes, RevokedPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.OnlyOutcomeAndEvaluatedClockAddedAsync(fixture, before, DeniedId, denied.EvaluatedAt);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AuthorityAsync(fixture, directory, catalog);
        return new(denied, result, bytes);
    }

    private static async Task RegrantAndColdAsync(PhysicalShardCatalogFixture fixture,
        ReplicatedOperation original, OperationResult originalResult, byte[] originalOutcome,
        DeniedReceipt denied, byte[] directory, byte[] catalog)
    {
        await SetAsync(fixture, true, RegrantedPolicy);
        var regrantedImage = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var regrantedCut = fixture.Store.Position;
        await PhysicalOwnerRegistrationPolicyReplayAssertions.PolicyReplayAsync(fixture, original, originalOutcome);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.PolicyReplayAsync(fixture, denied.Operation, denied.Outcome);
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, regrantedImage, regrantedCut);
        var before = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var fresh = Operation(fixture, FreshId, PhysicalOwnerDirectoryWholeFlow.InitialRevision);
        var success = fixture.Database.Apply(fresh);
        await Assert.That(success.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(success).SequenceEqual(NativeSerialization.Serialize(originalResult))).IsTrue();
        await PhysicalOwnerRegistrationPolicyReplayAssertions.OnlyOutcomeAndEvaluatedClockAddedAsync(fixture, before, FreshId, fresh.EvaluatedAt);
        var freshOutcome = PhysicalOwnerRegistrationPolicyReplayAssertions.Outcome(fixture, FreshId);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, fresh, success, freshOutcome, RegrantedPolicy);
        var image = PhysicalShardCatalogPolicyOutcomeAssertions.Image(fixture);
        var cut = fixture.Store.Position;
        fixture.Reopen();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PersistedAsync(fixture, true, RegrantedPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.PolicyReplayAsync(fixture, original, originalOutcome);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.PolicyReplayAsync(fixture, denied.Operation, denied.Outcome);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, original, originalResult, originalOutcome, OriginalPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, denied.Operation, denied.Result, denied.Outcome, RevokedPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, fresh, success, freshOutcome, RegrantedPolicy);
        await Assert.That(NativeSerialization.Serialize(fixture.Database.Apply(fresh)).SequenceEqual(NativeSerialization.Serialize(success))).IsTrue();
        await PhysicalShardCatalogPolicyOutcomeAssertions.UnchangedAsync(fixture, image, cut);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AuthorityAsync(fixture, directory, catalog);
        await ContinueAsync(fixture, success, directory, catalog);
    }

    private static async Task ContinueAsync(PhysicalShardCatalogFixture fixture,
        OperationResult expected, byte[] directory, byte[] catalog)
    {
        var before = PhysicalOwnerDirectoryWholeFlow.Image(fixture);
        var continuation = Operation(fixture, ContinuationId, PhysicalOwnerDirectoryWholeFlow.InitialRevision);
        var result = fixture.Database.Apply(continuation);
        await Assert.That(result.Error).IsNull();
        await Assert.That(NativeSerialization.Serialize(result).SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
        await PhysicalOwnerRegistrationPolicyReplayAssertions.OnlyOutcomeAndEvaluatedClockAddedAsync(fixture, before, ContinuationId, continuation.EvaluatedAt);
        var outcome = PhysicalOwnerRegistrationPolicyReplayAssertions.Outcome(fixture, ContinuationId);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.StoredAsync(fixture, continuation, result, outcome, RegrantedPolicy);
        await PhysicalOwnerRegistrationPolicyReplayAssertions.AuthorityAsync(fixture, directory, catalog);
    }

    private static ReplicatedOperation Operation(PhysicalShardCatalogFixture fixture, Guid id, long revision)
        => PhysicalOwnerDirectoryWholeFlow.Operation(fixture, PhysicalOwnerDirectoryWholeFlow.Request(revision), id, PrincipalId);

    private static async Task SetAsync(PhysicalShardCatalogFixture fixture, bool allowed, long epoch)
    {
        var principal = Principal(allowed, epoch);
        var result = fixture.Database.Apply(new(Guid.NewGuid(), OperationKind.ConfigurePrincipal,
            PhysicalShardCatalogFixture.RootPrincipalId, fixture.Database.EvaluationClock.GetUtcNow(),
            JsonSerializer.Serialize(new ConfigurePrincipalRequest(principal), JsonDefaults.Options)));
        await Assert.That(result.Error).IsNull();
        await PersistedAsync(fixture, allowed, epoch);
    }

    private static async Task PersistedAsync(PhysicalShardCatalogFixture fixture, bool allowed, long epoch)
    {
        var actual = fixture.Store.Read(view => view.GetRecord<PrincipalRecord>(KeyLoad.Core.KeySpace.Principal(PrincipalId)));
        await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(NativeSerialization.Serialize(Principal(allowed, epoch)))).IsTrue();
    }

    private static PrincipalRecord Principal(bool allowed, long epoch)
        => new(PrincipalId, PhysicalOwnerDirectoryWholeFlow.TenantId, [], [])
        {
            ClusterAdministrator = allowed,
            PolicyEpoch = epoch
        };

    private sealed record DeniedReceipt(ReplicatedOperation Operation, OperationResult Result, byte[] Outcome);
}
