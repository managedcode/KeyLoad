using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeR33PublicSelectionAssertions
{
    private const string SelectedProperty = "r33Public";
    private const string RejectedProperty = "r33PublicUnsupportedCount";
    private const int ExpectedRefusals = 6;
    private const string Filter = "/*/*/(TypedEndpointJoinRf3Tests|DeclarativeCompositionReverseQuotaRf3Tests|SubscriptionPhasePublicColdTests|BlobSupersessionPublicTests|BlobPartialReadBoundaryPublicTests|NativeTextProtectedPublicColdTests|CurrentOwnershipTokenAfterMovementRf3Tests)/*";

    internal static async Task AssertAsync(JsonElement root)
    {
        await NativeTUnitSelectionTests.AssertLocalSelectorAsync(root.GetProperty(SelectedProperty), Filter).ConfigureAwait(false);
        await Assert.That(root.GetProperty(RejectedProperty).GetInt32()).IsEqualTo(ExpectedRefusals);
    }
}
