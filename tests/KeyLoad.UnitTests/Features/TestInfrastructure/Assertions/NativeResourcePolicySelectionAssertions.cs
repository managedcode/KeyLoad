using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeResourcePolicySelectionAssertions
{
    private const string SelectedProperty = "resourcePolicy";
    private const string RejectedProperty = "resourcePolicyUnsupportedCount";
    private const int ExpectedRefusals = 4;
    private const string Filter = "/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3";

    internal static async Task AssertAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SelectedProperty), Filter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(RejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }
}
