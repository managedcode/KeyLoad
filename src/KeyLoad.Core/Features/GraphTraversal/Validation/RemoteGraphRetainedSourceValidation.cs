using System.Security.Cryptography;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void ValidateRemoteGraphRetainedSourceResult(IKeyValueView view, PrincipalRecord principal,
        RemoteGraphSourceReadRequestV1 request, RemoteGraphSourceResultV1 retained)
    {
        var command = retained.Original;
        if (command.Version != RemoteGraphProtocol.Version || command.ExpectedOwnerRevision < RemoteGraphAbsentRevision
            || command.ExpectedOwnerRevision == long.MaxValue || retained.Receipt.Token is null
            || retained.Receipt.Mutations.IsDefault || retained.Receipt.Mutations.Length != RemoteGraphSinglePreparation
            || retained.Intents.IsDefaultOrEmpty || retained.Intents.Length > Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        var mutation = retained.Receipt.Mutations[RemoteGraphFirstPreparation];
        var expected = command.Original.Upsert is null ? MutationDiscriminatorNames.DeleteEdge : MutationDiscriminatorNames.UpsertEdge;
        if (mutation is null || mutation.Kind != expected || mutation.Resource != request.Graph
            || mutation.Id != request.EdgeId || mutation.Revision != command.ExpectedOwnerRevision + RemoteGraphFirstGeneration)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        var targets = new HashSet<EntityRef>();
        foreach (var intent in retained.Intents)
        {
            RequireRemoteGraphStoredIntent(view, request, intent);
            if (intent.SourceCommandId != request.OriginalCommandId || intent.Content.Revision != mutation.Revision
                || intent.Content.OriginalPrincipalId != principal.Id || intent.Content.OriginalPolicyEpoch != principal.PolicyEpoch
                || !targets.Add(intent.Content.Destination)
                || !NativeSerialization.Serialize(intent.SourceCommit).AsSpan().SequenceEqual(
                    NativeSerialization.Serialize(retained.Receipt.Token)))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
        }
        if (command.Preparations.IsDefault || command.Preparations.Length != retained.Intents.Length)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        foreach (var preparation in command.Preparations)
        {
            if (preparation?.Preparation?.Request is null || !retained.Intents.Any(intent =>
                intent.Content.Destination == preparation.Preparation.Request.Destination
                && intent.Content.Deleted == preparation.Preparation.Request.Deleted))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
        }
        ValidateRemoteGraphRetainedPreparations(principal, request.SourceOwner, command);
    }

    private void ValidateRemoteGraphRetainedPreparations(PrincipalRecord principal, PhysicalShardRecord sourceOwner,
        RemoteGraphSourceMutationV1 command)
    {
        if (command.Preparations.IsDefault || command.Preparations.Length > Limits.MaxScanRecords)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        var digest = Convert.ToHexStringLower(SHA256.HashData(NativeSerialization.Serialize(command.Original)));
        var targets = new HashSet<EntityRef>();
        foreach (var verified in command.Preparations)
        {
            if (verified?.Preparation?.Request is null || string.IsNullOrEmpty(verified.Token)
                || !targets.Add(verified.Preparation.Request.Destination))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
            RemoteGraphPreparationClaimsV1 claims;
            try
            {
                RequireRemoteGraphPreparation(command.Original, principal, sourceOwner, verified.Preparation);
                claims = Verify<RemoteGraphPreparationClaimsV1>(verified.Token);
            }
            catch (KeyLoadException error)
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid, error);
            }
            if (claims?.Preparation is null || claims.Purpose != RemoteGraphProtocol.PreparationPurpose
                || claims.SourcePrincipalId != principal.Id || claims.SourcePolicyEpoch != principal.PolicyEpoch
                || claims.OriginalRequestDigest != digest
                || !NativeSerialization.Serialize(claims.Preparation).AsSpan().SequenceEqual(
                    NativeSerialization.Serialize(verified.Preparation)))
            {
                throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
            }
        }
    }
}
