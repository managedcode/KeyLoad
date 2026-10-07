using System.Runtime.CompilerServices;
using KeyLoad.AppHost.Hosting;

namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>Composes original collector preparation without a second standalone suite owner.</summary>
internal static class FixtureOwnedNativeCoveragePreparationComposition
{
    // Aspire resolves original AppHost DCP metadata through this assembly's native factory frame.
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static TBuilder Create<TBuilder>(Func<TBuilder> createNativeBuilder)
        where TBuilder : IDistributedApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(createNativeBuilder);
        _ = SerializationExecutionRegistration.Process.Value;
        return createNativeBuilder();
    }

    internal static void Compose(IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var runtime = AppHostOptionsRegistration.Get(builder);
        if (runtime.Control.Value.Tests is not { NativeCoverageRf3: not null, LocalRf3ImageEnabled: false }
            || !runtime.NativeCoverageRf3Image.Value.IsOuterSelection)
        {
            throw new InvalidOperationException(NativeCoverageRf3Protocol.InvalidSelection);
        }
        KeyLoadAppHostApplication.AddKeyLoad(builder);
    }
}
