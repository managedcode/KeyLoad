using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class FollowerDocumentReadTests
{
    private const long ZeroLag = 0;
    private const string MissingCredential = "The credential is unavailable or expired.";

    [Test]
    public async Task CapturedCommittedDocumentRetainsExplicitOldCutAndMinimumBeforeFullFreshContinuation()
    {
        using var fixture = new FollowerDocumentFixture();
        var dataPosition = fixture.Position;
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        var fresh = fixture.Put(FollowerDocumentFixture.FreshJson, FollowerDocumentFixture.FirstRevision);
        var currentPosition = fixture.Position;
        var actual = fixture.Complete(request with { MinimumToken = captured.Token }, captured);
        await FollowerDocumentAssertions.FullAsync(fixture, actual, dataPosition, currentPosition,
            FollowerDocumentFixture.FirstRevision, FollowerDocumentFixture.OriginalJson, fixture.Principal);
        await FollowerDocumentAssertions.CurrentAsync(fixture, FollowerDocumentFixture.SecondRevision,
            FollowerDocumentFixture.FreshJson);
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture,
            () => fixture.Complete(request with { MinimumToken = fresh.Token }, captured), ErrorCode.HistoryUnavailable,
            DatabaseEngine.FollowerReadMinimumUnavailable);
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture,
            () => fixture.Complete(request with { MaximumLagPositions = ZeroLag }, captured), ErrorCode.HistoryUnavailable,
            DatabaseEngine.FollowerReadLagExceeded);
        var renewed = fixture.Capture(fixture.Request(ZeroLag, fresh.Token));
        await FollowerDocumentAssertions.FullAsync(fixture,
            fixture.Complete(fixture.Request(ZeroLag, fresh.Token), renewed), currentPosition, currentPosition,
            FollowerDocumentFixture.SecondRevision, FollowerDocumentFixture.FreshJson, fixture.Principal);
    }

    [Test]
    [Arguments("revoked")]
    [Arguments("deleted")]
    [Arguments("replaced")]
    [Arguments("retargeted")]
    [Arguments("expired")]
    public async Task CapturedPrivateDocumentRequiresCurrentActualCredentialThenRestoredKeyContinues(string change)
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        var originalBytes = fixture.CredentialBytes();
        if (change == "deleted")
        { fixture.Db.Store.Commit((tx, _) => { tx.Delete(KeySpace.ApiKey(FollowerDocumentFixture.KeyId)); return true; }); }
        else
        {
            var replacement = change switch
            {
                "revoked" => fixture.Credential with { Revoked = true },
                "retargeted" => fixture.Credential with { PrincipalId = FollowerDocumentFixture.Root },
                "expired" => fixture.Credential with { ExpiresAt = fixture.Db.Database.EvaluationClock.GetUtcNow().AddTicks(-1) },
                "replaced" => DatabaseEngine.Credential(FollowerDocumentFixture.KeyId, FollowerDocumentFixture.Reader,
                    "follower-read-key.replacement-private-credential"),
                _ => throw new ArgumentOutOfRangeException(nameof(change))
            };
            fixture.Apply(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(replacement)).Get<bool>();
        }
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture, () => fixture.Complete(request, captured),
            ErrorCode.Unauthenticated, MissingCredential);
        if (change == "deleted")
        { fixture.Db.Store.Commit((tx, _) => { tx.Put(KeySpace.ApiKey(FollowerDocumentFixture.KeyId), originalBytes); return true; }); }
        else
        { fixture.Apply(OperationKind.ConfigureApiKey, new ConfigureApiKeyRequest(fixture.Credential)).Get<bool>(); }
        fixture.Put(FollowerDocumentFixture.FreshJson, FollowerDocumentFixture.FirstRevision);
        var renewed = fixture.Capture(request);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, renewed), fixture.Position,
            fixture.Position, FollowerDocumentFixture.SecondRevision, FollowerDocumentFixture.FreshJson, fixture.Principal);
        await FollowerDocumentAssertions.CurrentAsync(fixture, FollowerDocumentFixture.SecondRevision,
            FollowerDocumentFixture.FreshJson);
    }

    [Test]
    public async Task CapturedPrivateDocumentUsesRenewedGrantAndChangedFieldPolicyBeforeHealthyResume()
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var dataPosition = fixture.Position;
        var captured = fixture.Capture(request);
        var revoked = fixture.Principal with { Grants = [], PolicyEpoch = fixture.Principal.PolicyEpoch + 1 };
        fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked)).Get<PrincipalRecord>();
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture, () => fixture.Complete(request, captured),
            ErrorCode.PermissionDenied);
        var restored = fixture.Principal with { PolicyEpoch = revoked.PolicyEpoch + 1 };
        fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(restored)).Get<PrincipalRecord>();
        fixture.ChangePolicy();
        fixture.Put(FollowerDocumentFixture.FreshJson, FollowerDocumentFixture.FirstRevision);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, captured), dataPosition,
            fixture.Position, FollowerDocumentFixture.FirstRevision, FollowerDocumentFixture.RedactedOriginalJson,
            restored, redacted: true);
        var granted = restored with { FieldGrants = ["secret.read"], PolicyEpoch = restored.PolicyEpoch + 1 };
        fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(granted)).Get<PrincipalRecord>();
        var renewed = fixture.Capture(request);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, renewed), fixture.Position,
            fixture.Position, FollowerDocumentFixture.SecondRevision, FollowerDocumentFixture.FreshJson, granted);
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task CurrentDeletedOrInvisibleRowCannotExposeAnOlderCapturedPrivateRow(bool deleted)
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var dataPosition = fixture.Position;
        var captured = fixture.Capture(request);
        var principal = fixture.Principal;
        if (deleted)
        {
            fixture.Delete(FollowerDocumentFixture.FirstRevision);
        }
        else
        {
            fixture.Put(FollowerDocumentFixture.FreshJson, FollowerDocumentFixture.FirstRevision,
                new RowAccess("other-owner"));
            principal = fixture.Principal with
            {
                RestrictRows = true,
                OwnerId = "reader-owner",
                PolicyEpoch = fixture.Principal.PolicyEpoch + 1
            };
            fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        }
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, captured), dataPosition,
            fixture.Position, FollowerDocumentFixture.FirstRevision, null, principal);
        var restored = fixture.Principal with { PolicyEpoch = principal.PolicyEpoch + 1 };
        fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(restored)).Get<PrincipalRecord>();
        fixture.Put(FollowerDocumentFixture.FreshJson, FollowerDocumentFixture.SecondRevision);
        var renewed = fixture.Capture(request);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, renewed), fixture.Position,
            fixture.Position, FollowerDocumentFixture.SecondRevision + 1, FollowerDocumentFixture.FreshJson, restored);
    }

    [Test]
    public async Task InvalidMinimumOwnerGenerationAndCancellationLeaveCompleteNativeStateThenHealthyRead()
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture,
            () => fixture.Complete(request with { MinimumToken = captured.Token with { Incarnation = Guid.NewGuid() } }, captured),
            ErrorCode.TokenInvalidated, "The document session token belongs to another incarnation.");
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture,
            () => fixture.Complete(request, captured with { ReadGeneration = captured.ReadGeneration + 1 }),
            ErrorCode.OwnershipLost, DatabaseEngine.FollowerReadOwnerChanged);
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture,
            () => fixture.Complete(request, captured with { Owner = captured.Owner with { PlacementEpoch = captured.Owner.PlacementEpoch + 1 } }),
            ErrorCode.OwnershipLost, DatabaseEngine.FollowerReadOwnerChanged);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var before = fixture.State();
        var position = fixture.Db.Store.Position;
        var failure = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Complete(request, captured, cancellation.Token));
        await Assert.That(failure.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(fixture.State().SequenceEqual(before)).IsTrue();
        await Assert.That(fixture.Db.Store.Position).IsEqualTo(position);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, captured), fixture.Position,
            fixture.Position, FollowerDocumentFixture.FirstRevision, FollowerDocumentFixture.OriginalJson, fixture.Principal);
    }
}
