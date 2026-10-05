using System.Threading.Channels;

namespace KeyLoad.Comparisons;

internal static class OpenLoopChannel
{
    internal static Channel<OpenLoopWorkItem> Create(int capacity)
        => Channel.CreateBounded<OpenLoopWorkItem>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
}
