using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.StorageRecovery;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.UnitTests.Features.OrleansExecution;

internal sealed class SiloOwnerPurposeRegistrationTests
{
    [Test]
    public async Task DisposingActualRegistrationContainerDoesNotDrainBorrowedRequestOwner()
    {
        using var fixture = new PartitionHostRecoveryFixture();
        var runtime = ServerRuntimeTestOptions.Runtime(fixture.Options);
        await using var applicationServices = new ServiceCollection().BuildServiceProvider();
        using var admission = new RuntimeJournalAdmission();
        await using var node = new OrleansNode(applicationServices, runtime.Node, NullLoggerFactory.Instance, runtime, admission);
        var builder = WebApplication.CreateBuilder([]);
        builder.Services.AddSingleton(node);
        OrleansApplicationServices.RegisterSiloOwners(builder);
        RequestWorkOwner original;
        Func<RequestWorkOwner> borrow;
        await using (var services = builder.Services.BuildServiceProvider())
        {
            borrow = services.GetRequiredService<Func<RequestWorkOwner>>();
            original = borrow();
            await Assert.That(ReferenceEquals(original, node.RequestWork)).IsTrue();
            await Assert.That(ReferenceEquals(borrow(), original)).IsTrue();
            await Assert.That(services.GetService<RequestWorkOwner>()).IsNull();
        }
        await Assert.That(ReferenceEquals(borrow(), original)).IsTrue();
        using (original.Acquire(Guid.NewGuid(), RequestWorkKind.ReadCapability))
        {
            await Assert.That(original.IsJoined).IsFalse();
        }
        await node.StopAsync(CancellationToken.None);
        await Assert.That(original.IsJoined).IsTrue();
        await Assert.That(node.HasJoinedRequestWork).IsTrue();
    }
}
