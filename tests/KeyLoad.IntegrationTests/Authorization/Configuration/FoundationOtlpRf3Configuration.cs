using Aspire.Hosting.Testing;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class FoundationOtlpRf3Configuration
{
    internal static FoundationOtlpRf3Selection Validate(FoundationOtlpRf3Selection selection)
        => selection == FoundationOtlpRf3Selection.Dashboard ? selection
            : throw new ArgumentOutOfRangeException(nameof(selection));

    internal static DistributedApplicationTestingBuilderOptions Options(FoundationOtlpRf3Selection selection)
    {
        _ = Validate(selection);
        return new()
        {
            EnableDashboard = true
        };
    }
}
