using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3PairOperations
{
    private const int NoTerminals = 0;
    private const int OneTerminal = 1;
    private const string Invalid = "The original two-offer Tail stream returned an invalid terminal or progress frame.";
    internal static async Task<EventFeedControlResult> SdkAsync(KeyLoadClient sdk, EventFeedControlRequest request,
        EventFeedTailRf3Pair progress, Task resumeConsumer, CancellationToken token)
    {
        EventFeedControlResult? terminal = null;
        var terminals = NoTerminals;
        await foreach (var frame in sdk.TailEventFeedAsync(request, token).ConfigureAwait(false))
        {
            if (frame.Kind == CqrsStreamChunkKind.Progress && frame.ProgressResult is { IsSuccess: true, Value: { } value })
            {
                progress.ReportSdk(value);
                await resumeConsumer.WaitAsync(token).ConfigureAwait(false);
            }
            else if (frame.Final is { } final)
            {
                terminal = await McpCallerAssertions.SdkSuccessAsync(final).ConfigureAwait(false);
                terminals++;
            }
            else
            { throw new InvalidOperationException(Invalid); }
        }
        await Assert.That(terminals).IsEqualTo(OneTerminal);
        return terminal ?? throw new InvalidOperationException(Invalid);
    }

}
