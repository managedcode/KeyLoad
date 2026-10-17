using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Search;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
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
        var registrations = new ServiceCollection();
        var connection = new NativeConnectionOwnerIdentity();
        var borrow = new Bm25PortableMovementBorrow();
        var borrowed = new Bm25PortableMovementPurposeEvidence(0, borrow);
        var explicitObserver = new NativeSignedPurposeObservation();
        if (selection == 1)
        {
            registrations.AddSingleton<IGrainRequestPurposeObserver>(explicitObserver);
        }
        IGrainPartitionMovementSealedOperationObserver original = selection == 2 ? borrow : borrowed;
        registrations.AddSingleton(connection);
        registrations.AddSingleton(original);
        OrleansNativePeerServices.RegisterObservers(registrations, original);
        using var services = registrations.BuildServiceProvider();
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
        await Assert.That(registrations.Count(descriptor =>
            descriptor.ServiceType == typeof(IGrainRequestPurposeObserver))).IsEqualTo(selection == 2 ? 0 : 1);
        await Assert.That(borrow.IsSelected).IsFalse();
    }

    [Test]
    public async Task DisposingActualRegistrationContainerDoesNotDrainBorrowedNativeRequestOwner()
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var runtime = ServerRuntimeTestOptions.Runtime(fixture.Options);
        await using var applicationServices = new ServiceCollection().BuildServiceProvider();
        await using var node = new OrleansNode(applicationServices, runtime.Node, NullLoggerFactory.Instance, runtime);
        var builder = WebApplication.CreateBuilder([]);
        builder.Services.AddSingleton(node);
        OrleansNativePeerServices.RegisterSiloOwners(builder, fixture.Options, null);
        NativeRequestWorkOwner original;
        Func<NativeRequestWorkOwner> borrow;
        await using (var services = builder.Services.BuildServiceProvider())
        {
            borrow = services.GetRequiredService<Func<NativeRequestWorkOwner>>();
            original = borrow();
            await Assert.That(ReferenceEquals(original, node.RequestWork)).IsTrue();
            await Assert.That(ReferenceEquals(borrow(), original)).IsTrue();
            await Assert.That(services.GetService<NativeRequestWorkOwner>()).IsNull();
        }
        await Assert.That(ReferenceEquals(borrow(), original)).IsTrue();
        using (original.Acquire(Guid.NewGuid(), NativeRequestWorkKind.ReadCapability))
        {
            await Assert.That(original.IsJoined).IsFalse();
        }
        await node.StopAsync(CancellationToken.None);
        await Assert.That(original.IsJoined).IsTrue();
        await Assert.That(node.HasJoinedRequestWork).IsTrue();
    }
}
