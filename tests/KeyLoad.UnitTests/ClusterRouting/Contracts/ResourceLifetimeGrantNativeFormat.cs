using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.UnitTests.Features.InternalSerialization;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Exercises strict native root refusal with genuine owner-valid packets and unchanged real owners.</summary>
internal static class ResourceLifetimeGrantNativeFormat
{
    private const string MissingDurableOutcome = "The applied command has no durable outcome.";

    internal static async Task ExecuteAsync(ResourceLifetimeGrantState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var source = ControlledPartitionMovementExpiryOwnerState.Capture(state.Source);
        var target = ControlledPartitionMovementExpiryOwnerState.Capture(state.Target);
        var envelope = state.Request.Envelope with
        {
            Nonce = Guid.NewGuid()
        };
        var principal = PartitionMovementControlPrincipal.Resolve(state.Target.Database, envelope);
        var valid = state.Target.Database.CreateVerifiedPartitionMovementOperation(state.Record.EffectId,
            principal, state.Target.Database.EvaluationClock.GetUtcNow(), envelope, state.Proof);
        var root = PartitionMovementNativePayload.ReadAuthenticated(valid);
        await PartitionMovementNativeIdentityAssertions.VerifyAsync(valid, state.Target.Database.Limits);
        var duplicate = valid with
        {
            PayloadJson = JsonSerializer.Serialize(root.Original, JsonDefaults.Options)
        };
        var duplicateError = Assert.ThrowsExactly<KeyLoadException>(() => state.Target.Database.VerifyOperationAuthority(duplicate));
        await Assert.That(duplicateError.Code).IsEqualTo(ErrorCode.Corruption);
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
        ReadOnlyMemory<byte>[] variants =
        [
            NativeSerialization.Serialize(root.Original),
            NativeSerialization.Serialize(root with
            {
                LifetimeProofs = []
            }),
            NativeSerialization.Serialize(root with
            {
                LifetimeProofs = [state.Proof, state.Proof]
            })
        ];
        foreach (var value in variants)
        {
            token.ThrowIfCancellationRequested();
            var changed = Resign(valid, value, state.Target.Store.Identity.SigningKey.Span);
            var error = Assert.ThrowsExactly<KeyLoadException>(() => state.Target.Database.VerifyOperationAuthority(changed));
            await Assert.That(error.Code).IsEqualTo(ErrorCode.Corruption);
            await source.AssertUnchangedAsync(state.Source);
            await target.AssertUnchangedAsync(state.Target);
        }
        token.ThrowIfCancellationRequested();
        var current = state.Target.Database.ResolveOutcome(valid);
        await OutcomeAsync(current, ErrorCode.RecoveryRequired);
        await Assert.That(current.SafeDetail).IsEqualTo(MissingDurableOutcome);
        await source.AssertUnchangedAsync(state.Source);
        await target.AssertUnchangedAsync(state.Target);
    }

    private static ReplicatedOperation Resign(ReplicatedOperation original, ReadOnlyMemory<byte> value,
        ReadOnlySpan<byte> signingKey)
    {
        var payload = NativeAuthorityFixture.Read(original) with
        {
            Value = value
        };
        var claims = NativeSerialization.Deserialize<NativeCommandAuthority>(payload.Authority.Span) with
        {
            ValueHash = SHA256.HashData(value.Span)
        };
        var authority = NativeSerialization.Serialize(claims);
        return NativeAuthorityFixture.Wrap(original, payload with
        {
            Authority = authority,
            Signature = HMACSHA256.HashData(signingKey, authority)
        });
    }

    private static async Task OutcomeAsync(OperationResult result, ErrorCode expected)
    {
        await Assert.That(result.Error).IsEqualTo(expected);
        await Assert.That(result.NativeValue).IsNull();
        await Assert.That(result.Json).IsNull();
    }
}
