using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record RemoteTransferStartupCallbacks(
    Action<DistributedApplication, ContainerResource[]> Attach,
    Func<Task> Ready,
    Action<Exception, bool> Failed,
    Func<Task> Join);
