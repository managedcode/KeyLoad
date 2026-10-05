using System.Text;
using KeyLoad.CrashHost.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class C1OutcomeInspectionProtocolTests
{
    private const string DuplicateVersionField = "\"Version\":1,";
    private const string UnexpectedField = ",\"Unexpected\":true";
    private const string TrailingJson = "{}";
    private const byte JsonWhitespace = (byte)' ';
    private const string MissingFieldsJson = "{\"Version\":1}";
    private const string NullRequestJson = "null";

    [Test]
    public async Task AcCrs005MalformedAndExcessInputIsRejectedWithoutReceipt()
    {
        await C1OutcomeInspectionFixture.RunOwnedAsync(async fixture =>
        {
            var valid = C1OutcomeInspectionAssertions.ValidInput(fixture);
            var validJson = Encoding.UTF8.GetString(valid);
            var duplicate = Encoding.UTF8.GetBytes("{" + DuplicateVersionField + validJson[1..]);
            var unknown = Encoding.UTF8.GetBytes(validJson[..^1] + UnexpectedField + "}");
            var trailing = Encoding.UTF8.GetBytes(validJson + TrailingJson);
            var missingFields = Encoding.UTF8.GetBytes(MissingFieldsJson);
            var nullRequest = Encoding.UTF8.GetBytes(NullRequestJson);
            var malformedUtf8 = new byte[] { 0xC3, 0x28 };
            var oversized = new byte[C1OutcomeInspectionProtocol.MaximumRequestBytes + 1];
            oversized[0] = (byte)'{';
            Array.Fill(oversized, (byte)' ', 1, oversized.Length - 1);
            var exactBoundary = new byte[C1OutcomeInspectionProtocol.MaximumRequestBytes];
            valid.CopyTo(exactBoundary, 0);
            Array.Fill(exactBoundary, JsonWhitespace, valid.Length, exactBoundary.Length - valid.Length);

            var accepted = await C1OutcomeInspectionAssertions.AssertJoinedAsync(
                await C1OutcomeInspectionAssertions.RunRawAsync(fixture, exactBoundary));
            await C1OutcomeInspectionAssertions.AssertReceiptAsync(fixture, expected: true, accepted);
            await AssertRawRejectedAsync(fixture, duplicate);
            await AssertRawRejectedAsync(fixture, unknown);
            await AssertRawRejectedAsync(fixture, trailing);
            await AssertRawRejectedAsync(fixture, missingFields);
            await AssertRawRejectedAsync(fixture, nullRequest);
            await AssertRawRejectedAsync(fixture, malformedUtf8);
            await AssertRawRejectedAsync(fixture, oversized);
            await C1OutcomeInspectionAssertions.AssertOuterOwnerReleasedAsync(fixture);
        }).ConfigureAwait(false);
    }

    private static async Task AssertRawRejectedAsync(C1OutcomeInspectionFixture fixture, byte[] input)
    {
        var result = await C1OutcomeInspectionAssertions.RunRawAsync(fixture, input);
        await C1OutcomeInspectionAssertions.AssertRejectedAsync(result);
    }
}
