using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsProbeCodecTests
{
    [Test]
    public async Task AcCrs004ReadsEveryPrivateRecordKindPhaseAndAction()
    {
        var owner = RequestCqrsProbeCodecInput.Owner();
        var parsedOwner = RequestCqrsProbeJson.ReadOwner(owner);
        await Assert.That(parsedOwner.Version).IsEqualTo(RequestCqrsProbeProtocol.Version);
        await Assert.That(parsedOwner.Kind).IsEqualTo(RequestCqrsProbeProtocol.OwnerKind);
        await Assert.That(parsedOwner.SessionId).IsEqualTo(RequestCqrsProbeCodecInput.SessionId);

        var release = RequestCqrsProbeJson.ReadRelease(RequestCqrsProbeCodecInput.Release());
        await Assert.That(release.RequestId).IsEqualTo(RequestCqrsProbeCodecInput.RequestId);
        var readMarker = RequestCqrsProbeJson.ReadMarker(
            RequestCqrsProbeCodecInput.Marker(commandId: Guid.Empty));
        await Assert.That(readMarker.CommandId).IsEqualTo(Guid.Empty);

        foreach (var phase in RequestCqrsProbeCodecInput.MarkerPhases)
        {
            foreach (var outcome in RequestCqrsProbeCodecInput.Outcomes)
            {
                var marker = RequestCqrsProbeJson.ReadMarker(RequestCqrsProbeCodecInput.Marker(phase: phase, outcome: outcome));
                await Assert.That(marker.Phase).IsEqualTo(phase);
                await Assert.That(marker.Outcome).IsEqualTo(outcome);
            }
        }

        foreach (var phase in RequestCqrsProbeCodecInput.ActivePhases)
        {
            foreach (var action in RequestCqrsProbeCodecInput.Actions)
            {
                var arm = RequestCqrsProbeJson.ReadArm(RequestCqrsProbeCodecInput.Arm(phase, action));
                await Assert.That(arm.Phase).IsEqualTo(phase);
                await Assert.That(arm.Action).IsEqualTo(action);
                await Assert.That(arm.CommandId).IsEqualTo(RequestCqrsProbeCodecInput.CommandId);
            }
        }

        var readArm = RequestCqrsProbeJson.ReadArm(RequestCqrsProbeCodecInput.Arm(
            RequestCqrsProbePhase.RequestStarted, RequestCqrsProbeAction.Hold, Guid.Empty, GrainReadKind.Document));
        await Assert.That(readArm.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(readArm.ReadKind).IsEqualTo(GrainReadKind.Document);
    }

    [Test]
    public async Task AcCrs004RejectsUnknownDuplicateCasedNestedAndMissingFields()
    {
        var owner = RequestCqrsProbeCodecInput.Owner();
        var invalidOwners = new[]
        {
            RequestCqrsProbeCodecInput.InsertFirstProperty(owner, RequestCqrsProbeCodecInput.UnknownProperty, "1"),
            RequestCqrsProbeCodecInput.InsertFirstProperty(owner, RequestCqrsProbeCodecInput.VersionProperty, "1"),
            RequestCqrsProbeCodecInput.Replace(owner, RequestCqrsProbeCodecInput.VersionToken, RequestCqrsProbeCodecInput.LowerVersionToken),
            RequestCqrsProbeCodecInput.Replace(owner, RequestCqrsProbeCodecInput.VersionToken, RequestCqrsProbeCodecInput.EscapedVersionToken),
            RequestCqrsProbeCodecInput.Replace(owner, RequestCqrsProbeCodecInput.OwnerKindToken, RequestCqrsProbeCodecInput.InvalidOwnerKindToken),
            RequestCqrsProbeCodecInput.Replace(owner, RequestCqrsProbeCodecInput.OwnerVersionToken, RequestCqrsProbeCodecInput.InvalidOwnerVersionToken),
            RequestCqrsProbeCodecInput.Replace(owner, RequestCqrsProbeCodecInput.VoterValue, RequestCqrsProbeCodecInput.NestedVoterValue),
            RequestCqrsProbeCodecInput.RemoveProperty(owner, RequestCqrsProbeCodecInput.VoterProperty)
        };
        foreach (var invalid in invalidOwners)
        {
            await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync(invalid);
        }

        var integerEnum = RequestCqrsProbeCodecInput.Replace(RequestCqrsProbeCodecInput.Arm(),
            RequestCqrsProbeCodecInput.HoldActionToken, RequestCqrsProbeCodecInput.IntegerActionToken);
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(integerEnum);
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Replace(
            RequestCqrsProbeCodecInput.Arm(), RequestCqrsProbeCodecInput.HoldActionToken,
            RequestCqrsProbeCodecInput.InvalidActionToken));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Replace(
            RequestCqrsProbeCodecInput.Arm(), RequestCqrsProbeCodecInput.RequestStartedPhaseToken,
            RequestCqrsProbeCodecInput.InvalidPhaseToken));
        await RequestCqrsProbeCodecAssertions.InvalidReleaseAsync(RequestCqrsProbeCodecInput.InsertFirstProperty(
            RequestCqrsProbeCodecInput.Release(), RequestCqrsProbeCodecInput.ArmIdProperty,
            RequestCqrsProbeCodecInput.ArmIdValue));
        await RequestCqrsProbeCodecAssertions.InvalidMarkerAsync(RequestCqrsProbeCodecInput.Replace(
            RequestCqrsProbeCodecInput.Marker(), RequestCqrsProbeCodecInput.ObservedOutcomeToken,
            RequestCqrsProbeCodecInput.InvalidOutcomeToken));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.RemoveProperty(
            RequestCqrsProbeCodecInput.Arm(), RequestCqrsProbeCodecInput.ActionProperty));
        await RequestCqrsProbeCodecAssertions.InvalidReleaseAsync(RequestCqrsProbeCodecInput.RemoveProperty(
            RequestCqrsProbeCodecInput.Release(), RequestCqrsProbeCodecInput.RequestIdProperty));
        await RequestCqrsProbeCodecAssertions.InvalidMarkerAsync(RequestCqrsProbeCodecInput.RemoveProperty(
            RequestCqrsProbeCodecInput.Marker(), RequestCqrsProbeCodecInput.SiloAddressProperty));
        var nullRequired = RequestCqrsProbeCodecInput.Replace(owner, RequestCqrsProbeCodecInput.VoterValue,
            RequestCqrsProbeCodecInput.NullVoterValue);
        await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync(nullRequired);
    }

    [Test]
    public async Task AcCrs004RejectsInvalidIdsPrincipalBoundsUtf8AndFraming()
    {
        await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync(RequestCqrsProbeCodecInput.Owner(Guid.Empty.ToString("N")));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Arm(armId: Guid.Empty));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Arm(phase: RequestCqrsProbePhase.ProducerDisposed));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Arm(commandId: RequestCqrsProbeCodecInput.CommandId, readKind: GrainReadKind.Document));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Arm(commandId: Guid.Empty));
        await RequestCqrsProbeCodecAssertions.InvalidArmAsync(RequestCqrsProbeCodecInput.Arm(principalId: RequestCqrsProbeCodecInput.OverlongPrincipal));
        await RequestCqrsProbeCodecAssertions.InvalidReleaseAsync(RequestCqrsProbeCodecInput.Release(Guid.Empty));
        await RequestCqrsProbeCodecAssertions.InvalidMarkerAsync(RequestCqrsProbeCodecInput.Marker(armId: Guid.Empty));
        await RequestCqrsProbeCodecAssertions.InvalidMarkerAsync(RequestCqrsProbeCodecInput.Marker(requestId: Guid.Empty));
        await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync([0xFF]);

        var owner = RequestCqrsProbeCodecInput.Owner();
        await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync(owner[..^1]);
        await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync(RequestCqrsProbeCodecInput.Append(owner, RequestCqrsProbeCodecInput.TrailingObject));
        var maxPrincipal = RequestCqrsProbeCodecInput.Arm(principalId: RequestCqrsProbeCodecInput.MaximumPrincipal);
        var parsed = RequestCqrsProbeJson.ReadArm(maxPrincipal);
        await Assert.That(parsed.PrincipalId).IsEqualTo(RequestCqrsProbeCodecInput.MaximumPrincipal);
    }

    [Test]
    public async Task AcCrs004Accepts8192BytesAndRejects8193Bytes()
    {
        var exactlyMaximum = RequestCqrsProbeCodecInput.PadOwner(RequestCqrsProbeProtocol.MaximumRecordBytes);
        var parsed = RequestCqrsProbeJson.ReadOwner(exactlyMaximum);
        await Assert.That(parsed.SessionId).IsEqualTo(RequestCqrsProbeCodecInput.SessionId);
        await Assert.That(exactlyMaximum.Length).IsEqualTo(RequestCqrsProbeProtocol.MaximumRecordBytes);

        var excess = RequestCqrsProbeCodecInput.Append(exactlyMaximum, RequestCqrsProbeCodecInput.Space);
        await RequestCqrsProbeCodecAssertions.InvalidOwnerAsync(excess);
    }
}
