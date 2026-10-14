using System.Text;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ClusterRouting.Processes;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1SubmissionOutcomeInspectionProtocolTests
{
    private const int OverLimitBytes = 1;
    private const int MutatedNodeByteIndex = 0;
    private const byte MutatedNodeBit = 1;
    private static readonly byte[] MalformedUtf8 = [0xC3, 0x28];
    private const string MissingPrincipal = "c1-probe-missing-inspection-outcome";
    private const string DuplicateVersion = "\"Version\":1,";
    private const string Unexpected = ",\"Unexpected\":true";
    private const string MissingFields = "{\"Version\":1}";
    private const string Trailing = "{}";

    [Test]
    public async Task GenuineChildRejectsMalformedRequestsAndPreservesOriginalOutcome()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var input = C1SubmissionOutcomeInspectionProtocol.SerializeRequest(Request(fixture));
            var json = Encoding.UTF8.GetString(input);
            var malformed = new byte[][]
            {
                Encoding.UTF8.GetBytes("{" + DuplicateVersion + json[1..]),
                Encoding.UTF8.GetBytes(json[..^1] + Unexpected + "}"),
                Encoding.UTF8.GetBytes(json + Trailing), Encoding.UTF8.GetBytes(MissingFields),
                MalformedUtf8, new byte[C1SubmissionOutcomeInspectionProtocol.MaximumRequestBytes + OverLimitBytes]
            };
            fixture.CloseStore();
            foreach (var bytes in malformed)
            {
                var phase = bytes.Length > C1SubmissionOutcomeInspectionProtocol.MaximumRequestBytes
                    ? C1OutcomeInspectionFailurePhase.ReadInput : C1OutcomeInspectionFailurePhase.ValidateRequest;
                await AssertRejectedAsync(fixture, bytes, phase).ConfigureAwait(false);
            }
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true).ConfigureAwait(false);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    [Test]
    public async Task GenuineChildRejectsMissingScopedOutcomeAndWrongOriginalOwner()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var request = Request(fixture);
            fixture.CloseStore();
            await AssertRejectedAsync(fixture, C1SubmissionOutcomeInspectionProtocol.SerializeRequest(request),
                C1OutcomeInspectionFailurePhase.ReadOutcome).ConfigureAwait(false);
            var nodeBytes = request.ExpectedNodeId.ToByteArray();
            nodeBytes[MutatedNodeByteIndex] ^= MutatedNodeBit;
            var wrongOwner = request with
            {
                ExpectedNodeId = new Guid(nodeBytes)
            };
            await AssertRejectedAsync(fixture, C1SubmissionOutcomeInspectionProtocol.SerializeRequest(wrongOwner),
                C1OutcomeInspectionFailurePhase.OpenStore).ConfigureAwait(false);
            await C1OutcomeInspectionAssertions.AssertOutcomeAsync(fixture, expected: true).ConfigureAwait(false);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private static C1SubmissionOutcomeInspectionRequest Request(C1OutcomeInspectionFixture fixture)
        => new(C1SubmissionOutcomeInspectionProtocol.Version, fixture.DirectoryPath, fixture.Identity.NodeId,
            fixture.Identity.Incarnation, MissingPrincipal, fixture.CommandId, fixture.Partition);

    private static async Task AssertRejectedAsync(C1OutcomeInspectionFixture fixture, byte[] bytes,
        C1OutcomeInspectionFailurePhase phase)
    {
        var result = await C1OutcomeInspectionProcess.RunSubmissionOutcomeAsync(bytes, fixture.OuterOwnerLockPath,
            TestContext.Current!.Execution.CancellationToken).ConfigureAwait(false);
        C1OutcomeInspectionOwnerPhase.ObserveChild(fixture, result);
        await C1OutcomeInspectionAssertions.AssertRejectedAsync(result, phase).ConfigureAwait(false);
    }
}
