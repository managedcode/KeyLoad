using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: actual native binding and validation reach an executing snapshot consumer.</summary>
internal sealed class TypedOptionsExecutionFlowTests
{
    [Test]
    public async Task BoundOverridesReachNativeAdmissionAndTaskDeadlineAsync()
    {
        var state = new TypedOptionsFlowState();
        using var provider = TypedOptionsFlowRegistration.CreateProvider(state, "3", "00:00:02");
        var consumer = provider.GetRequiredService<TypedOptionsFlowConsumer>();

        var result = await consumer.ExecuteAsync(42);

        await Assert.That(result).IsEqualTo(42);
        await Assert.That(state.AdmittedOperations).IsEqualTo(1);
        await Assert.That(state.InitialPermits).IsEqualTo(3);
        await Assert.That(state.ObservedDeadline).IsEqualTo(TimeSpan.FromSeconds(2));
    }

    [Test]
    public async Task CanonicalDefaultsReachTheSameNativeOperationAsync()
    {
        var state = new TypedOptionsFlowState();
        using var provider = TypedOptionsFlowRegistration.CreateProvider(state);
        var consumer = provider.GetRequiredService<TypedOptionsFlowConsumer>();

        var result = await consumer.ExecuteAsync(7);

        await Assert.That(result).IsEqualTo(7);
        await Assert.That(state.AdmittedOperations).IsEqualTo(1);
        await Assert.That(state.InitialPermits).IsEqualTo(64);
        await Assert.That(state.ObservedDeadline).IsEqualTo(TimeSpan.FromSeconds(30));
    }

    [Test]
    [Arguments("0", "00:00:02")]
    [Arguments("3", "00:00:00")]
    public async Task InvalidBoundPolicyFailsBeforeConsumerAdmissionAsync(string capacity, string deadline)
    {
        var state = new TypedOptionsFlowState();
        using var provider = TypedOptionsFlowRegistration.CreateProvider(state, capacity, deadline);

        Assert.ThrowsExactly<OptionsValidationException>(() => provider.GetRequiredService<TypedOptionsFlowConsumer>());

        await Assert.That(state.AdmittedOperations).IsEqualTo(0);
        await Assert.That(state.InitialPermits).IsEqualTo(0);
        await Assert.That(state.ObservedDeadline).IsEqualTo(TimeSpan.Zero);
    }
}
