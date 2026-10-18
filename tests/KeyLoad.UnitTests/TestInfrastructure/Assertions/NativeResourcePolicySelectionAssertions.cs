using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeResourcePolicySelectionAssertions
{
    private const string SelectedProperty = "resourcePolicy";
    private const string RejectedProperty = "resourcePolicyUnsupportedCount";
    private const int ExpectedRefusals = 4;
    private const string Filter = "/*/*/ResourcePolicyUpdateClusterTests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster";

    internal static async Task AssertAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SelectedProperty), Filter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(RejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }
}
