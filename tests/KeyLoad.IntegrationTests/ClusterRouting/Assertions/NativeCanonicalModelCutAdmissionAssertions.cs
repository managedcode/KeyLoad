using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutAdmissionAssertions
{
    private const string WrongPrincipal = "native-cut-refused-principal";
    private const string Invalid = "The genuine native cut admission checks require the exact original six-owner scope.";

    internal static async Task RequireAsync(TwoRf3MembershipWave wave, string adminCredential,
        NativeCanonicalModelCutRequest originalScope, IReadOnlyDictionary<string, ReplicaSiloDiscovery> owners,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> secrets,
        IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> before, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(wave);
        ArgumentException.ThrowIfNullOrWhiteSpace(adminCredential);
        NativeCanonicalModelCutCodec.ValidateRequest(originalScope);
        if (!token.CanBeCanceled || before.Count != TwoRf3MembershipProtocol.NodeCount
            || originalScope.SessionId != wave.QueryControls.SessionId
            || !TwoRf3MembershipProtocol.Nodes.All(before.ContainsKey))
        {
            throw new InvalidDataException(Invalid);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            NativeCanonicalModelCutAssertions.Require(before[node].Request, owners[node], before[node], token);
            if ((before[node].Request with
            {
                Nonce = originalScope.Nonce
            }) != originalScope)
            {
                throw new InvalidDataException(Invalid);
            }
        }
        foreach (var mutation in Mutations(originalScope))
        {
            NativeCanonicalModelCutCodec.ValidateRequest(mutation);
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                token.ThrowIfCancellationRequested();
                await NativeCanonicalModelCutHttpRefusal.RequireValidationAsync(wave, node,
                    adminCredential, mutation with
                    {
                        Nonce = Guid.NewGuid()
                    }, token).ConfigureAwait(false);
            }
            var healthy = await NativeCanonicalModelCutClient.ReadSixAsync(wave, adminCredential,
                originalScope, owners, secrets, token).ConfigureAwait(false);
            foreach (var node in TwoRf3MembershipProtocol.Nodes)
            {
                NativeCanonicalModelCutAssertions.RequireUnchanged(before[node], healthy[node], token);
            }
        }
    }

    internal static async Task RequireRetiredOriginalAsync(TwoRf3MembershipWave wave, string adminCredential,
        NativeCanonicalModelCutRequest originalScope, IReadOnlyDictionary<string, ReplicaSiloDiscovery> owners,
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> secrets, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!token.CanBeCanceled || originalScope.SessionId != wave.QueryControls.SessionId)
        {
            throw new InvalidDataException(Invalid);
        }
        var bytes = NativeCanonicalModelCutOriginalArm.Read(wave.QueryControls, originalScope.ArmId);
        NativeCanonicalModelCutRetainedArmAssertions.RequireSettledOriginal(wave.QueryControls,
            originalScope.ArmId, bytes);
        await wave.QueryControls.RetireArmAsync(originalScope.ArmId, token).ConfigureAwait(false);
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            await NativeCanonicalModelCutHttpRefusal.RequireValidationAsync(wave, node, adminCredential,
                originalScope with
                {
                    Nonce = Guid.NewGuid()
                }, token).ConfigureAwait(false);
        }
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var expected = owners[node];
            var actual = await TwoRf3MembershipSignedDiscovery.ReadAsync(wave.Application, node,
                expected.ClusterId, expected.Incarnation, secrets[node], adminCredential, token).ConfigureAwait(false);
            await Assert.That(NativeSerialization.Serialize(actual).SequenceEqual(
                NativeSerialization.Serialize(expected))).IsTrue();
        }
        await Assert.That(wave.QueryControls.ArmFor(originalScope.ArmId).Retired).IsTrue();
        token.ThrowIfCancellationRequested();
    }

    private static IEnumerable<NativeCanonicalModelCutRequest> Mutations(NativeCanonicalModelCutRequest original)
    {
        yield return original with
        {
            ArmId = Different(original.ArmId)
        };
        yield return original with
        {
            SessionId = Different(Guid.ParseExact(original.SessionId, "N")).ToString("N")
        };
        yield return original with
        {
            AcceptCommandId = Different(original.AcceptCommandId)
        };
        yield return original with
        {
            LogicalPrincipalId = original.LogicalPrincipalId == WrongPrincipal
            ? WrongPrincipal + "-other" : WrongPrincipal
        };
    }

    private static Guid Different(Guid original)
    {
        Guid changed;
        do
        {
            changed = Guid.NewGuid();
        } while (changed == original || changed == Guid.Empty);
        return changed;
    }
}
