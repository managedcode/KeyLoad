using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Borrows the fixture's registered clock and centrally validated protected movement policy.</summary>
internal sealed class ProtectedDocumentRf3Timing
{
    private readonly TestExecutionOptions execution;

    internal ProtectedDocumentRf3Timing(TwoRf3MembershipWave wave)
    {
        execution = wave.Application.Services.GetRequiredService<IOptions<TestExecutionOptions>>().Value;
        if (!execution.IsValid())
        {
            throw new OptionsValidationException(TestExecutionOptions.SectionName, typeof(TestExecutionOptions),
            [TestExecutionOptions.ValidationMessage]);
        }
        Clock = wave.Application.Services.GetRequiredService<TimeProvider>();
    }

    internal TimeProvider Clock { get; }
    internal DateTimeOffset SetupExpiry => Clock.GetUtcNow() + execution.ProtectedMovementSetupRequestLifetime;
    internal DateTimeOffset OutcomeExpiry => Clock.GetUtcNow() + execution.ProtectedMovementOutcomeRequestLifetime;
    internal TimeSpan CleanupTimeout => execution.ProtectedMovementCaptureCleanupTimeout;
}
