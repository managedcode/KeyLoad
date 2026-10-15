using System.Security.Cryptography;
using System.Text;
using KeyLoad.Clustering;
using KeyLoad.UnitTests.Features.ClientApi;
using KeyLoad.UnitTests.Features.Messaging;
using D = KeyLoad.UnitTests.Features.ClientApi.DeclaredResponseEofProtocol;
using P = KeyLoad.UnitTests.Features.ClusterRouting.NativeConnectionIdentityCollisionProtocol;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class NativeConnectionIdentityCollisionAssertions
{
    internal static NativeConnectionIdentityCollisionCut Capture(NativeConnectionIdentityCollisionState state)
    {
        var database = state.Scenario.Fixture.Database;
        return new(QueueWholeFlowStorage.Bytes(database.Store).Select(row =>
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(row)))).ToArray(),
            database.Store.Position, database.ReadJournalCut());
    }

    internal static async Task HeldAsync(NativeConnectionIdentityCollisionState state,
        NativeConnectionIdentityCollisionCut before, ConnectionObservedOperation original)
    {
        var actual = Capture(state);
        await Assert.That(actual.RowHashes.SequenceEqual(before.RowHashes)).IsTrue();
        await Assert.That(actual.Position).IsEqualTo(before.Position);
        await Assert.That(actual.Journal).IsEqualTo(before.Journal);
        await Assert.That(ReferenceEquals(original, state.Scenario.Observation.Read(state.OriginalRequest))).IsTrue();
        await ProbeAsync(state, state.OriginalRequest, state.First, state.Original);
        await Assert.That(original.Disposed.Task.IsCompleted).IsFalse();
        await ConnectionNativeAssertions.MissingOutcomeAsync(state.Scenario, state.First, state.Original, D.Document);
        await ConnectionNativeAssertions.MissingOutcomeAsync(state.Scenario, state.Second, state.Following, P.SecondDocument);
        await DeclaredResponseEofAssertions.EmptyAsync(state.Scenario);
    }

    internal static async Task ProbeAsync(NativeConnectionIdentityCollisionState state, Guid requestId,
        PrincipalRecord principal, CommandRequest command)
    {
        var actual = state.Scenario.Observation.Read(requestId);
        var expected = new GrainRequestProbeIdentity(requestId, command.CommandId, principal.Id, null, OperationKind.Batch);
        await Assert.That(actual.Identity).IsEqualTo(expected);
        await Assert.That(actual.ActivationId.IsDefault).IsFalse();
    }
}
