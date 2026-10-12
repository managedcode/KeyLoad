using KeyLoad.Orleans;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Search;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class SiloOwnerPurposeRegistrationTests
{
    [Test]
    [Arguments(0)]
    [Arguments(1)]
    [Arguments(2)]
    public async Task ActualSiloRegistrationPreservesBorrowedAndExplicitPurposeOwnerIdentity(int selection)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        var connection = new NativeConnectionOwnerIdentity();
        var borrow = new Bm25PortableMovementBorrow();
        var borrowed = new Bm25PortableMovementPurposeEvidence(0, borrow);
        var explicitObserver = new NativeSignedPurposeObservation();
        if (selection == 1)
        { builder.Services.AddSingleton<IGrainRequestPurposeObserver>(explicitObserver); }
        IGrainPartitionMovementSealedOperationObserver original = selection == 2 ? borrow : borrowed;
        OrleansNativePeerServices.RegisterSiloOwners(builder, NullLoggerFactory.Instance,
            connection, discoveryOmission: null, original, localhostClustering: null);
        using var services = builder.Services.BuildServiceProvider();
        await Assert.That(ReferenceEquals(services.GetRequiredService<NativeConnectionOwnerIdentity>(), connection)).IsTrue();
        await Assert.That(ReferenceEquals(
            services.GetRequiredService<IGrainPartitionMovementSealedOperationObserver>(), original)).IsTrue();
        var actual = services.GetService<IGrainRequestPurposeObserver>();
        IGrainRequestPurposeObserver? expected = selection switch
        {
            0 => borrowed,
            1 => explicitObserver,
            2 => null,
            _ => throw new ArgumentOutOfRangeException(nameof(selection))
        };
        await Assert.That(ReferenceEquals(actual, expected)).IsTrue();
        await Assert.That(builder.Services.Count(descriptor =>
            descriptor.ServiceType == typeof(IGrainRequestPurposeObserver))).IsEqualTo(selection == 2 ? 0 : 1);
        await Assert.That(borrow.IsSelected).IsFalse();
    }
}
