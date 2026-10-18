using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class SiloOwnerPurposeRegistrationTests
{
    [Test]
    public async Task DisposingActualRegistrationContainerDoesNotDrainBorrowedNativeRequestOwner()
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var runtime = ServerRuntimeTestOptions.Runtime(fixture.Options);
        await using var applicationServices = new ServiceCollection().BuildServiceProvider();
        await using var node = new OrleansNode(applicationServices, runtime.Node, NullLoggerFactory.Instance, runtime);
        var builder = WebApplication.CreateBuilder([]);
        builder.Services.AddSingleton(node);
        OrleansNativePeerServices.RegisterSiloOwners(builder, null);
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
