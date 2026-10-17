using KeyLoad.Server.Features.GraphTraversal;

namespace KeyLoad.Server;

internal static class OrleansNodeRemoteGraphAccess
{
    internal static RemoteGraphNativeClient BorrowRemoteGraphClient(this OrleansNode node)
        => node.GraphResources.BorrowClient();
}
