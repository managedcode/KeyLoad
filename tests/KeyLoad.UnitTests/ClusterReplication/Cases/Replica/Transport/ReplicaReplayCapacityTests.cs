using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Replication;

namespace KeyLoad.UnitTests;

/// <summary>AC-REP-006: application saturation cannot spend the fixed-voter consensus reserve.</summary>
internal sealed class ReplicaReplayCapacityTests
{
    /// <summary>AC-REP-006: full forward/read/append pools still admit votes, snapshots, noops and control operations.</summary>
    [Test]
    public async Task SaturatedApplicationPoolsPreserveIndependentConsensusAndControlCapacity()
    {
        using var fixture = new ReplicaSecurityFixture();
        foreach (var request in new[] { fixture.Forward(OperationKind.Batch), fixture.Read(), fixture.Append(OperationKind.Batch) })
        {
            fixture.Receiver.VerifyRequest(request);
            await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(
                fixture.Resign(request with
                {
                    Nonce = Guid.NewGuid()
                }))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        }
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(
            fixture.Append(OperationKind.BootstrapPhysicalShardCatalog, OperationKind.Batch))).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        fixture.Receiver.VerifyRequest(fixture.Vote());
        fixture.Receiver.VerifyRequest(fixture.Append());
        fixture.Receiver.VerifyRequest(fixture.Append((OperationKind?)null));
        foreach (var kind in new[] { OperationKind.Delivery, OperationKind.SubscriptionDelivery, OperationKind.BootstrapPhysicalShardCatalog, OperationKind.SetDispatch })
        {
            fixture.Receiver.VerifyRequest(fixture.Forward(kind));
            fixture.Receiver.VerifyRequest(fixture.Append(kind));
        }
        var transferId = Guid.NewGuid();
        var image = new ReplicaSnapshot(transferId, fixture.Configuration.Incarnation, 1, 1, 1,
            Convert.ToHexStringLower(SHA256.HashData([ReplicaSecurityFixture.SnapshotByte])),
            transferId.ToString(ReplicaTransportProtocol.NonceFormat) + ReplicaProtocol.SnapshotExtension);
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.SnapshotBegin, new SnapshotBeginRequest(ReplicaSecurityFixture.VoterA, 1, image)));
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.SnapshotChunk,
            new SnapshotChunkRequest(ReplicaSecurityFixture.VoterA, 1, image.TransferId, 0, new byte[] { ReplicaSecurityFixture.SnapshotByte })));
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.SnapshotComplete,
            new SnapshotCompleteRequest(ReplicaSecurityFixture.VoterA, 1, image.TransferId)));
    }

    /// <summary>AC-REP-006: a properly resigned cross-method reuse remains authentication failure even when data is full.</summary>
    [Test]
    public async Task NonceReuseCannotMoveBetweenApplicationAndCriticalMethods()
    {
        using var fixture = new ReplicaSecurityFixture();
        var original = fixture.Forward(OperationKind.Batch);
        fixture.Receiver.VerifyRequest(original);
        var read = fixture.Resign(fixture.Read() with
        {
            Nonce = original.Nonce
        });
        var vote = fixture.Resign(fixture.Vote() with
        {
            Nonce = original.Nonce
        });
        var malformed = fixture.Resign(original with
        {
            Payload = ReplicaForwardTestFrame.WrapOperation(ReplicaSecurityWireFixture.Operation(
                ReplicaForwardTestFrame.ReadOperation(original.Payload), ReplicaSecurityFixture.MissingOperationFields))
        });
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(read)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(vote)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(original)).Code).IsEqualTo(ErrorCode.Unauthenticated);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(malformed)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }

    /// <summary>AC-REP-006: one fixed peer cannot consume another peer's data or consensus capacity.</summary>
    [Test]
    public async Task FixedVoterCapacityIsIsolatedAndEveryCriticalPoolIsBounded()
    {
        using var fixture = new ReplicaSecurityFixture(new()
        {
            CriticalPerVoter = 1,
            ForwardPerVoter = 1,
            ReadBarrierPerVoter = 1,
            DataAppendPerVoter = 1
        });
        fixture.Receiver.VerifyRequest(fixture.Forward(OperationKind.Batch));
        fixture.Receiver.VerifyRequest(fixture.Resign(fixture.Forward(OperationKind.Batch) with
        {
            Sender = ReplicaSecurityFixture.VoterC
        }));
        fixture.Receiver.VerifyRequest(fixture.Vote());
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(fixture.Vote())).Code).IsEqualTo(ErrorCode.ResourceExhausted);
        fixture.Receiver.VerifyRequest(fixture.Request(ReplicaRpc.RequestVote, new VoteRequest(ReplicaSecurityFixture.VoterC, 1, 0, 0), ReplicaSecurityFixture.VoterC));
    }

    /// <summary>AC-REP-006: duplicate, unknown, missing and invalid classifier fields cannot claim control capacity.</summary>
    [Test]
    [Arguments(ReplicaSecurityFixture.DuplicateKind)]
    [Arguments(ReplicaSecurityFixture.UnknownKind)]
    [Arguments(ReplicaSecurityFixture.MissingOperationFields)]
    [Arguments(ReplicaSecurityFixture.InvalidPayload)]
    [Arguments(ReplicaSecurityFixture.UnknownField)]
    public async Task MalformedSignedControlPayloadDoesNotConsumeReserve(string scenario)
    {
        using var fixture = new ReplicaSecurityFixture(new()
        {
            CriticalPerVoter = 1
        });
        var invalid = fixture.InvalidControl(scenario);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid)).Code).IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>AC-REP-006: duplicate append entries or operations cannot masquerade as an empty/noop append.</summary>
    [Test]
    [Arguments(true)]
    [Arguments(false)]
    public async Task DuplicateAppendFieldsCannotConsumeCriticalCapacity(bool operation)
    {
        using var fixture = new ReplicaSecurityFixture(new()
        {
            CriticalPerVoter = 1
        });
        var invalid = fixture.DuplicateAppend(operation);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(invalid)).Code).IsEqualTo(ErrorCode.Validation);
        fixture.Receiver.VerifyRequest(fixture.Vote());
    }

    /// <summary>AC-REP-006: genuine concurrent verifications admit one nonce exactly once.</summary>
    [Test]
    public async Task ConcurrentNonceReuseAdmitsExactlyOneRequest()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Vote();
        var results = await Task.WhenAll(Enumerable.Range(0, ReplicaSecurityFixture.ReservedCapacity).Select(_ => Task.Run(() =>
        {
            try
            {
                fixture.Receiver.VerifyRequest(request);
                return (ErrorCode?)null;
            }
            catch (KeyLoadException error) { return error.Code; }
        })));
        await Assert.That(results.Count(result => result is null)).IsEqualTo(1);
        await Assert.That(results.Count(result => result == ErrorCode.Unauthenticated)).IsEqualTo(ReplicaSecurityFixture.ReservedCapacity - 1);
    }

    /// <summary>AC-REP-006: independently owned native bytes retain control classification and current operation content.</summary>
    [Test]
    public async Task ValidOwnedNativeBytesRetainControlClassificationAndCurrentOperationContent()
    {
        using var fixture = new ReplicaSecurityFixture();
        var request = fixture.Forward(OperationKind.BootstrapPhysicalShardCatalog);
        var operation = ReplicaForwardTestFrame.ReadOperation(request.Payload);
        request = fixture.Resign(request with
        {
            Payload = ReplicaForwardTestFrame.Payload(operation)
        });
        fixture.Receiver.VerifyRequest(request);
        await Assert.That(ReplicaForwardTestFrame.ReadOperation(request.Payload).PayloadJson).IsEqualTo(ReplicaSecurityFixture.OperationPayload);
    }

    /// <summary>AC-REP-006: warmed borrowed classification avoids decoded content; accepted requests own independent buffers.</summary>
    [Test]
    public async Task MaximumOperationClassificationDoesNotAllocateItsDecodedPayload()
    {
        using var fixture = new ReplicaSecurityFixture();
        var limits = new DatabaseLimits();
        var payload = JsonDefaults.Serialize(new string(ReplicaSecurityFixture.PayloadPadding, limits.MaxBatchBytes - ReplicaSecurityFixture.JsonStringQuoteBytes));
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.Batch, ReplicaSecurityFixture.VoterA,
            TimeProvider.System.GetUtcNow(), ReplicaTransportProtocol.Utf8.GetString(payload));
        operation = fixture.Database.NormalizeOperation(operation);
        var request = fixture.Request(ReplicaRpc.Forward, operation);
        _ = fixture.Receiver.ClassifyBorrowedRequest(fixture.Forward(OperationKind.Batch));
        var before = GC.GetAllocatedBytesForCurrentThread();
        var pool = fixture.Receiver.ClassifyBorrowedRequest(request);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        await Assert.That(allocated).IsLessThan(ReplicaSecurityFixture.MaximumClassificationAllocationBytes);
        await Assert.That(pool).IsEqualTo(ReplicaReplayPool.Forward);
        var owned = fixture.Receiver.VerifyRequest(request);
        await Assert.That(owned.Payload.Span.SequenceEqual(request.Payload.Span)).IsTrue();
        await Assert.That(owned.Signature.Span.SequenceEqual(request.Signature.Span)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(request.Payload, out var callerPayload)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(owned.Payload, out var ownedPayload)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(request.Signature, out var callerSignature)).IsTrue();
        await Assert.That(MemoryMarshal.TryGetArray(owned.Signature, out var ownedSignature)).IsTrue();
        await Assert.That(ReferenceEquals(callerPayload.Array, ownedPayload.Array)).IsFalse();
        await Assert.That(ReferenceEquals(callerSignature.Array, ownedSignature.Array)).IsFalse();
        var firstPayloadByte = owned.Payload.Span[0];
        var firstSignatureByte = owned.Signature.Span[0];
        callerPayload.Array![callerPayload.Offset] ^= ReplicaSecurityFixture.MutationBit;
        callerSignature.Array![callerSignature.Offset] ^= ReplicaSecurityFixture.MutationBit;
        await Assert.That(owned.Payload.Span[0]).IsEqualTo(firstPayloadByte);
        await Assert.That(owned.Signature.Span[0]).IsEqualTo(firstSignatureByte);
        await Assert.That(Assert.ThrowsExactly<KeyLoadException>(() => fixture.Receiver.VerifyRequest(owned)).Code).IsEqualTo(ErrorCode.Unauthenticated);
    }
}
