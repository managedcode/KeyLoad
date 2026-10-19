namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class FoundationOtlpClusterConfiguration
{
    internal static FoundationOtlpClusterSelection Validate(FoundationOtlpClusterSelection selection)
        => selection == FoundationOtlpClusterSelection.Dashboard ? selection
            : throw new ArgumentOutOfRangeException(nameof(selection));

}
