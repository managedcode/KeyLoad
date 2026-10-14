using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal ReplicatedOperation CreateVerifiedRemoteGraphSource(string localPrincipalId,
        RemoteGraphSourceMutationV1 verified, DateTimeOffset originalExpiresAt, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(verified);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (originalExpiresAt.Offset != TimeSpan.Zero || originalExpiresAt <= EvaluationClock.GetUtcNow()
            || NativeSerialization.Measure(verified) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        var command = Store.Read(view => SignRemoteGraphSourcePreparations(work.CreateView(view),
            localPrincipalId, verified, originalExpiresAt, work));
        var payload = NativeSerialization.Serialize(command);
        RequireNativeBudget(payload.Length);
        var identity = Identity<RemoteGraphSourceIdentityV1>(NativeSerialization.Serialize(command.Original));
        var result = IssueNativeOperation(new(command.Original.CommandId, OperationKind.RemoteGraphSource,
            localPrincipalId, EvaluationClock.GetUtcNow(), identity), new Features.InternalSerialization.NativeCommandPayload(payload));
        work.Check();
        return result;
    }

    private RemoteGraphSourceMutationV1 SignRemoteGraphSourcePreparations(IKeyValueView view,
        string principalId, RemoteGraphSourceMutationV1 verified, DateTimeOffset expiresAt, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        _ = RequireRemoteGraphSource(view, principal, verified);
        var originalBytes = NativeSerialization.Serialize(verified.Original);
        var digest = Convert.ToHexStringLower(SHA256.HashData(originalBytes));
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
        var preparations = ImmutableArray.CreateBuilder<RemoteGraphVerifiedPreparationV1>(verified.Preparations.Length);
        foreach (var verifiedPreparation in verified.Preparations)
        {
            work.Check();
            if (verifiedPreparation?.Preparation is null)
            {
                throw Errors.Fail(ErrorCode.Unauthenticated, RemoteGraphProtocol.Invalid);
            }
            RequireRemoteGraphPreparation(verified.Original, principal, catalog.DefaultShard,
                verifiedPreparation.Preparation);
            var claims = new RemoteGraphPreparationClaimsV1(RemoteGraphProtocol.PreparationPurpose,
                verifiedPreparation.Preparation, principal.Id, principal.PolicyEpoch, digest, expiresAt);
            preparations.Add(verifiedPreparation with
            {
                Token = Sign(claims)
            });
        }
        return verified with
        {
            Preparations = preparations.MoveToImmutable()
        };
    }
}
