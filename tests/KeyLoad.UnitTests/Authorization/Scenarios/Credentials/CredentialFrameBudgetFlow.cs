using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class CredentialFrameBudgetFlow
{
    private const string PrincipalId = "bounded-auth-subject";
    private const string CredentialId = "bounded-auth-key";
    private const string Secret = "bounded-auth-key.actual-unit-secret-32-characters";
    private const string Tenant = "tenant";
    private const int CredentialRecords = 2;
    private const long OneByte = 1;
    private const long FirstEpoch = 1;
    private const long RevokedEpoch = 2;
    private const long RepairedEpoch = 3;
    private const int MaximumImageRows = 64;
    private const string MissingMetadata = "The dispatched execution reply did not retain authenticated metadata.";

    internal static async Task RunAsync()
    {
        using var fixture = new TestDatabase();
        var subject = new PrincipalRecord(PrincipalId, Tenant, [], []) { PolicyEpoch = FirstEpoch };
        await Assert.That(Errors.Code(fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(subject)).Problem)).IsNull();
        await Assert.That(Errors.Code(fixture.Submit(OperationKind.ConfigureApiKey,
            new ConfigureApiKeyRequest(DatabaseEngine.Credential(CredentialId, PrincipalId, Secret))).Problem)).IsNull();
        var expected = fixture.Store.Read(view => checked(Size(view, KeySpace.ApiKey(CredentialId))
            + Size(view, KeySpace.Principal(PrincipalId))));
        var before = Image(fixture);
        var position = fixture.Store.Position;
        var shortWork = fixture.CreateReadWork(CancellationToken.None);
        shortWork.ConstrainWork(expected - OneByte, CredentialRecords);
        var denied = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Authenticate(
            Secret, fixture.Database.EvaluationClock.GetUtcNow(), shortWork));
        await Assert.That(denied.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await UnchangedAsync(fixture, before, position);
        var work = fixture.CreateReadWork(CancellationToken.None);
        work.ConstrainWork(expected, CredentialRecords);
        var principal = fixture.Database.Authenticate(Secret, fixture.Database.EvaluationClock.GetUtcNow(), work);
        await Assert.That(principal.Id).IsEqualTo(PrincipalId);
        await Assert.That(principal.ClusterAdministrator).IsFalse();
        await Assert.That(work.ReadBytes).IsEqualTo(expected);
        await Assert.That(work.ConstrainedRecords).IsEqualTo(CredentialRecords);
        var aggregate = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Authenticate(
            Secret, fixture.Database.EvaluationClock.GetUtcNow(), work));
        await Assert.That(aggregate.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        await UnchangedAsync(fixture, before, position);
        await CurrentPolicyAsync(fixture, subject);
        await IndeterminateAsync(fixture);
    }

    private static async Task CurrentPolicyAsync(TestDatabase fixture, PrincipalRecord subject)
    {
        await Assert.That(Errors.Code(fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(subject with
            {
                Revoked = true,
                PolicyEpoch = RevokedEpoch
            })).Problem)).IsNull();
        var before = Image(fixture);
        var position = fixture.Store.Position;
        var revoked = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Authenticate(Secret,
            fixture.Database.EvaluationClock.GetUtcNow(), fixture.CreateReadWork(CancellationToken.None)));
        await Assert.That(revoked.Code).IsEqualTo(ErrorCode.Unauthenticated);
        await UnchangedAsync(fixture, before, position);
        await Assert.That(Errors.Code(fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(subject with
            {
                PolicyEpoch = RepairedEpoch
            })).Problem)).IsNull();
        before = Image(fixture);
        position = fixture.Store.Position;
        var repaired = fixture.Database.Authenticate(Secret, fixture.Database.EvaluationClock.GetUtcNow(),
            fixture.CreateReadWork(CancellationToken.None));
        await Assert.That(repaired.Id).IsEqualTo(PrincipalId);
        await Assert.That(repaired.PolicyEpoch).IsEqualTo(RepairedEpoch);
        await UnchangedAsync(fixture, before, position);
    }

    // Primitive failure injection only: the actual credential/storage read is real; no execution transport success is claimed.
    private static async Task IndeterminateAsync(TestDatabase fixture)
    {
        var work = fixture.CreateReadWork(CancellationToken.None);
        var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
        using (work.EnterReadGrant(grant))
        {
            _ = fixture.Database.Authenticate(Secret, fixture.Database.EvaluationClock.GetUtcNow(), work);
        }

        var before = Image(fixture);
        var position = fixture.Store.Position;
        var bytes = work.ReadBytes;
        var records = work.ConstrainedRecords;
        var original = new IOException(MissingMetadata);
        work.FailIndeterminateMetadata(original);
        var repeated = Assert.ThrowsExactly<IOException>(() => fixture.Database.Authenticate(
            Secret, fixture.Database.EvaluationClock.GetUtcNow(), work));
        await Assert.That(ReferenceEquals(repeated, original)).IsTrue();
        await Assert.That(work.ReadBytes).IsEqualTo(bytes);
        await Assert.That(work.ConstrainedRecords).IsEqualTo(records);
        work.CompleteSettledReadGrant(grant);
        var afterSettlement = Assert.ThrowsExactly<IOException>(work.Check);
        await Assert.That(ReferenceEquals(afterSettlement, original)).IsTrue();
        await UnchangedAsync(fixture, before, position);
    }

    private static long Size(IKeyValueView view, byte[] key)
    {
        var value = view.ReadOwnedValue(key) ?? throw new InvalidOperationException(PrincipalId);
        return checked(key.LongLength + value.LongLength);
    }

    private static Dictionary<string, string> Image(TestDatabase fixture) => fixture.Store.Read(view =>
    {
        var page = view.Scan([], MaximumImageRows);
        if (page.HasMore)
        {
            throw new InvalidOperationException(PrincipalId);
        }

        return page.Records.ToDictionary(row => Convert.ToHexString(row.Key.Span), row => Convert.ToHexString(row.Value.Span));
    });

    private static async Task UnchangedAsync(TestDatabase fixture, Dictionary<string, string> expected, long position)
    {
        var actual = Image(fixture);
        await Assert.That(fixture.Store.Position).IsEqualTo(position);
        await Assert.That(actual.Count).IsEqualTo(expected.Count);
        foreach (var row in expected)
        {
            await Assert.That(actual[row.Key]).IsEqualTo(row.Value);
        }
    }
}
