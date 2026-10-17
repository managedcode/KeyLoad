using System.Runtime.CompilerServices;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Composes the original AppHost without resuming a standalone suite owner.</summary>
internal static class FixtureOwnedImagePreparationComposition
{
    private const string InvalidPreparationSelection = "Fixture-owned preparation requires the validated local RF3 image selection.";

    // Aspire's direct testing factory discovers the owning AppHost DCP metadata from this frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static TBuilder Create<TBuilder>(Func<TBuilder> createNativeBuilder)
        where TBuilder : IDistributedApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(createNativeBuilder);
        _ = SerializationExecutionRegistration.Process.Value;
        var builder = createNativeBuilder();
        if (AppHostOptionsRegistration.Get(builder).Control.Value.Tests is not { LocalRf3ImageEnabled: true })
        {
            throw new InvalidOperationException(InvalidPreparationSelection);
        }
        KeyLoadAppHostApplication.AddKeyLoad(builder);
        return builder;
    }
}
