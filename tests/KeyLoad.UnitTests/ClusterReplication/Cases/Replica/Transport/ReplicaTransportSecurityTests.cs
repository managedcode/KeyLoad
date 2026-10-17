using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: genuine MACs bind peer scope, method, bytes, runtime generation and nonce.</summary>
internal sealed class ReplicaTransportSecurityTests
{
    /// <summary>AC-REP-006: changed request fields cannot pass an unchanged genuine signature.</summary>
    [Test]
    [Arguments(ReplicaSecurityMutation.Payload)]
    [Arguments(ReplicaSecurityMutation.Method)]
    [Arguments(ReplicaSecurityMutation.Nonce)]
    [Arguments(ReplicaSecurityMutation.Generation)]
    [Arguments(ReplicaSecurityMutation.Incarnation)]
    [Arguments(ReplicaSecurityMutation.Voter)]
    public async Task RequestTamperOrWrongScopeCannotConsumeReservedCapacity(ReplicaSecurityMutation mutation)
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var changed = mutation switch
        {
            ReplicaSecurityMutation.Payload => request with { Payload = ReplicaSecurityFixture.EmptyRead },
            ReplicaSecurityMutation.Method => request with { Method = ReplicaRpc.ReadBarrier },
            ReplicaSecurityMutation.Nonce => request with { Nonce = Guid.NewGuid() },
            ReplicaSecurityMutation.Generation => request with { RuntimeAddress = fixture.OtherGeneration },
            ReplicaSecurityMutation.Incarnation => request with { Incarnation = Guid.NewGuid() },
            _ => request with { Sender = ReplicaSecurityFixture.UnknownVoter }
        };
        if (mutation is ReplicaSecurityMutation.Generation or ReplicaSecurityMutation.Incarnation or ReplicaSecurityMutation.Voter)
        {
            changed = fixture.Resign(changed);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(changed)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        fixture.Receiver.VerifyRequest(request);
    }

    /// <summary>AC-REP-006: errors are signed and their code, bytes, request nonce and generation cannot be substituted.</summary>
    [Test]
    [Arguments(ReplicaSecurityMutation.Payload)]
    [Arguments(ReplicaSecurityMutation.Method)]
    [Arguments(ReplicaSecurityMutation.Nonce)]
    [Arguments(ReplicaSecurityMutation.Generation)]
    [Arguments(ReplicaSecurityMutation.Incarnation)]
    [Arguments(ReplicaSecurityMutation.Voter)]
    [Arguments(ReplicaSecurityMutation.Error)]
    public async Task SignedCapacityFailureIsBoundToItsOriginatingRequest(ReplicaSecurityMutation mutation)
    {
        using var fixture = new ReplicaSecurityFixture();
        fixture.Receiver.VerifyRequest(fixture.Forward(OperationKind.Batch));
        var request = fixture.Forward(OperationKind.Batch);
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(request));
        var reply = fixture.Receiver.CreateReply(request, ReadOnlyMemory<byte>.Empty, failure.Code, failure.Message);
        fixture.Sender.VerifyReply(request, reply);
        await Assert.That(reply.Error).IsEqualTo(ErrorCode.ResourceExhausted);
        var changed = mutation switch
        {
            ReplicaSecurityMutation.Payload => reply with { Payload = ReplicaSecurityFixture.EmptyRead },
            ReplicaSecurityMutation.Method => reply with { Method = ReplicaRpc.ReadBarrier },
            ReplicaSecurityMutation.Nonce => reply with { RequestNonce = Guid.NewGuid() },
            ReplicaSecurityMutation.Generation => reply with { RuntimeAddress = fixture.OtherGeneration },
            ReplicaSecurityMutation.Incarnation => reply with { Incarnation = Guid.NewGuid() },
            ReplicaSecurityMutation.Voter => reply with { Sender = ReplicaSecurityFixture.UnknownVoter },
            _ => reply with { Error = ErrorCode.PermissionDenied }
        };
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyReply(request, changed)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-REP-006: discovery MACs reject substituted bytes, voters and GET nonces.</summary>
    [Test]
    public async Task DiscoverySignatureBindsExactBytesExpectedVoterAndRequestNonce()
    {
        using var fixture = new ReplicaSecurityFixture();
        var nonce = Guid.NewGuid();
        var bytes = ReplicaProtocolCodec.Serialize(fixture.Discovery.Read());
        var signature = fixture.Receiver.SignDiscovery(bytes, nonce);
        fixture.Sender.VerifyDiscovery(ReplicaSecurityFixture.VoterB, bytes, nonce, signature);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, bytes, Guid.NewGuid(), signature)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterC, bytes, nonce, signature)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Sender.VerifyDiscovery(
            ReplicaSecurityFixture.VoterB, ReplicaSecurityFixture.EmptyRead, nonce, signature)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-REP-006: a different real credential or cluster scope cannot verify a genuine envelope.</summary>
    [Test]
    public async Task DifferentCredentialOrClusterIdentityCannotVerifyRequest()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var credential = RandomNumberGenerator.GetBytes(ReplicaTransportProtocol.SecretBytes);
        var options = fixture.Options with
        {
            Secret = credential
        };
        using var wrongCredential = new ReplicaEnvelopeAuthenticator(UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration), UnitRoutingOptions.Peers(fixture.Configuration, options), fixture.Discovery, TimeProvider.System, UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(fixture.Options.ReplayLimits), canonicalDatabase: fixture.Database);
        using var wrongCluster = new ReplicaEnvelopeAuthenticator(UnitExecutionOptions.ReplicaConfiguration(fixture.Configuration), UnitRoutingOptions.Peers(fixture.Configuration, fixture.Options with
        {
            ClusterId = ReplicaSecurityFixture.OtherClusterId
        }), fixture.Discovery, TimeProvider.System, UnitRoutingOptions.Transport(), UnitRoutingOptions.Replay(fixture.Options.ReplayLimits), canonicalDatabase: fixture.Database);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => wrongCredential.VerifyRequest(request)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => wrongCluster.VerifyRequest(request)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        CryptographicOperations.ZeroMemory(credential);
    }
}
