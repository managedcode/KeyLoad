using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkSchedulingPairRf3Controls
{
    internal static async Task RequireAsync(RequestCqrsProbeFixture controls, Guid primaryId, Guid adjunctId)
    {
        var primary = Load(controls, primaryId);
        var adjunct = Load(controls, adjunctId);
        var original = new[] { primary, adjunct };
        Require(original, adjunct.Record);
        var originalPrimary = Convert.ToHexString(primary.ExactBytes);
        var originalAdjunct = Convert.ToHexString(adjunct.ExactBytes);
        var missing = new[] { adjunct };
        var foreign = adjunct with { Record = adjunct.Record with { SourceArmId = Guid.NewGuid() } };
        var wrongPrincipal = adjunct with { Record = adjunct.Record with { PrincipalId = primary.Record.PrincipalId + SampleChunkJobRevocationProtocol.Separator } };
        var duplicate = adjunct with { Record = adjunct.Record with { ArmId = Guid.NewGuid() } };
        await Assert.That(() => Require(missing, adjunct.Record)).Throws<InvalidOperationException>();
        await Assert.That(() => Require([primary, foreign], foreign.Record)).Throws<InvalidOperationException>();
        await Assert.That(() => Require([primary, wrongPrincipal], wrongPrincipal.Record)).Throws<InvalidOperationException>();
        await Assert.That(() => Require([primary, adjunct, duplicate], adjunct.Record)).Throws<InvalidOperationException>();
        await Assert.That(() => RequestCqrsSampleChunkProbePair.Select([primary, primary],
            GrainRequestPhase.AuthorizationReload)).Throws<InvalidOperationException>();
        // The same actual decoded pair remains valid after each refused copy; no fixture control was mutated.
        Require(original, adjunct.Record);
        await Assert.That(Convert.ToHexString(primary.ExactBytes)).IsEqualTo(originalPrimary);
        await Assert.That(Convert.ToHexString(adjunct.ExactBytes)).IsEqualTo(originalAdjunct);
        await Assert.That(RequestCqrsSampleChunkProbePair.Select(original,
            GrainRequestPhase.SampleChunkNativeJobReturned)).IsEqualTo(primary);
        await Assert.That(RequestCqrsSampleChunkProbePair.Select(original,
            GrainRequestPhase.AuthorizationReload)).IsEqualTo(adjunct);
        var identity = new GrainRequestProbeIdentity(Guid.NewGuid(), primary.Record.CommandId,
            primary.Record.PrincipalId, null, OperationKind.Batch);
        var single = new RequestCqrsProbeSnapshot([primary], [], [], OneArm, primary.ExactBytes.Length);
        await Assert.That(RequestCqrsProbeClaimSelection.Find(identity, single, RequestCqrsRf3Protocol.Node1,
            GrainRequestPhase.RequestStarted)).IsEqualTo(primary);
    }

    private const int OneArm = 1;

    private static RequestCqrsProbeLoadedArm Load(RequestCqrsProbeFixture controls, Guid id)
    {
        var bytes = controls.ArmFor(id).Bytes;
        return new(controls.Json.ReadArm(bytes), bytes);
    }

    private static void Require(RequestCqrsProbeLoadedArm[] originals, RequestCqrsProbeArmRecord adjunct)
        => RequestCqrsSampleChunkProbePair.RequireDeclaredPair(adjunct, originals);
}
