using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DocumentStorage;

internal sealed class FollowerDocumentAuthorityTests
{
    private const string CorruptVerifier = "A credential verifier is invalid.";
    private const string CorruptPosition = "The canonical applied position is invalid.";
    private const string Unavailable = "The credential is unavailable or expired.";
    private const string InvalidHex = "not-a-credential-verifier";

    [Test]
    [Arguments("missing")]
    [Arguments("negative")]
    public async Task InvalidCanonicalAppliedCutCannotReleaseCapturedDocumentAndRestoredAuthorityContinues(string change)
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        var original = fixture.AppliedBytes();
        fixture.Db.Store.Commit((tx, _) =>
        {
            if (change == "missing")
            { tx.Delete(KeySpace.AppliedBytes); }
            else
            { tx.Put(KeySpace.AppliedBytes, NativeSerialization.Serialize(-1L)); }
            return true;
        });
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture, () => fixture.Complete(request, captured),
            ErrorCode.Corruption, CorruptPosition);
        fixture.Db.Store.Commit((tx, _) => { tx.Put(KeySpace.AppliedBytes, original); return true; });
        await HealthyAsync(fixture, request);
    }

    [Test]
    public async Task CorruptNativeVerifierThenRevokedPrincipalCannotReleaseCapturedDocumentBeforeRestoration()
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        var original = fixture.CredentialBytes();
        fixture.Db.Store.Commit((tx, _) =>
        {
            tx.PutRecord(KeySpace.ApiKey(FollowerDocumentFixture.KeyId), fixture.Credential with { Verifier = InvalidHex });
            return true;
        });
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture, () => fixture.Complete(request, captured),
            ErrorCode.Corruption, CorruptVerifier);
        fixture.Db.Store.Commit((tx, _) => { tx.Put(KeySpace.ApiKey(FollowerDocumentFixture.KeyId), original); return true; });
        var revoked = fixture.Principal with { Revoked = true, PolicyEpoch = fixture.Principal.PolicyEpoch + 1 };
        fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(revoked)).Get<PrincipalRecord>();
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture, () => fixture.Complete(request, captured),
            ErrorCode.Unauthenticated, Unavailable);
        var restored = fixture.Principal with { PolicyEpoch = revoked.PolicyEpoch + 1 };
        fixture.Apply(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(restored)).Get<PrincipalRecord>();
        fixture.Put(FollowerDocumentFixture.FreshJson, FollowerDocumentFixture.FirstRevision);
        var renewed = fixture.Capture(request);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, renewed), fixture.Position,
            fixture.Position, FollowerDocumentFixture.SecondRevision, FollowerDocumentFixture.FreshJson, restored);
    }

    [Test]
    public async Task NativeWitnessRoundTripPreservesServerProofButAlteredProofFailsWithoutStorageEffects()
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        var witness = DatabaseEngine.IssueCredentialWitness(FollowerDocumentFixture.Secret);
        var original = new FollowerDocumentReadCapability(request, witness);
        var decoded = NativeSerialization.Deserialize<FollowerDocumentReadCapability>(NativeSerialization.Serialize(original))!;
        await Assert.That(JsonDefaults.Serialize(decoded.Request).SequenceEqual(JsonDefaults.Serialize(request))).IsTrue();
        await Assert.That(decoded.Credential.CredentialId).IsEqualTo(FollowerDocumentFixture.KeyId);
        await Assert.That(decoded.Credential.Digest.Span.SequenceEqual(witness.Digest.Span)).IsTrue();
        foreach (var proof in new[] { witness with { Digest = new byte[32] }, witness with { Digest = new byte[1] },
            witness with { CredentialId = "unregistered-key" } })
        {
            await FollowerDocumentAssertions.FailureUnchangedAsync(fixture,
                () => fixture.Db.Database.CompleteFollowerDocument(FollowerDocumentFixture.Reader,
                    new(request, proof), captured, fixture.ReplicaId, FollowerDocumentFixture.Term, default),
                ErrorCode.Unauthenticated, Unavailable);
        }
        var actual = fixture.Db.Database.CompleteFollowerDocument(FollowerDocumentFixture.Reader,
            decoded, captured, fixture.ReplicaId, FollowerDocumentFixture.Term, default);
        await FollowerDocumentAssertions.FullAsync(fixture, actual, fixture.Position, fixture.Position,
            FollowerDocumentFixture.FirstRevision, FollowerDocumentFixture.OriginalJson, fixture.Principal);
        await HealthyAsync(fixture, request);
    }

    [Test]
    [Arguments("version")]
    [Arguments("lag")]
    [Arguments("replica")]
    public async Task UnsupportedPublicSelectorLeavesFullNativeStateAndValidSelectorContinues(string change)
    {
        using var fixture = new FollowerDocumentFixture();
        var request = fixture.Request();
        var captured = fixture.Capture(request);
        var invalid = change switch
        {
            "version" => request with { Version = FollowerDocumentFixture.Version + 1 },
            "lag" => request with { MaximumLagPositions = -1 },
            "replica" => request with { ReplicaId = " " },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        await FollowerDocumentAssertions.FailureUnchangedAsync(fixture, () => fixture.Complete(invalid, captured),
            ErrorCode.Validation, DatabaseEngine.FollowerReadInvalid);
        await HealthyAsync(fixture, request);
    }

    private static async Task HealthyAsync(FollowerDocumentFixture fixture, ReadFollowerDocumentRequestV1 request)
    {
        var renewed = fixture.Capture(request);
        await FollowerDocumentAssertions.FullAsync(fixture, fixture.Complete(request, renewed), fixture.Position,
            fixture.Position, FollowerDocumentFixture.FirstRevision, FollowerDocumentFixture.OriginalJson, fixture.Principal);
        await FollowerDocumentAssertions.CurrentAsync(fixture, FollowerDocumentFixture.FirstRevision,
            FollowerDocumentFixture.OriginalJson);
    }
}
