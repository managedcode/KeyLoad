using KurrentDB.Client;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.Comparisons.Targets;

internal static class KurrentOwnedStreamAppend
{
    internal static async Task<IWriteResult> AppendAsync(KurrentDBClient writer, KurrentStreamOwnership ownership,
        string stream, KurrentEventData eventData, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(ownership);
        ArgumentNullException.ThrowIfNull(eventData);
        var runtimeEventId = eventData.EventId.ToGuid();
        ownership.Reserve(stream, runtimeEventId);
        IWriteResult result;
        try
        {
            result = await writer.AppendToStreamAsync(stream, StreamState.NoStream, [eventData], cancellationToken: token);
            if (result is not SuccessResult)
            {
                throw new ComparisonFailureException(KurrentConstants.AppendAcknowledgementMissing);
            }
        }
        catch (WrongExpectedVersionException)
        {
            ownership.Reject(stream, runtimeEventId);
            throw;
        }
        catch (Exception)
        {
            ownership.MarkUnknown(stream, runtimeEventId);
            throw;
        }
        // No conversion, copy validation or success result can precede this actual ACK transition.
        ownership.Acknowledge(stream, runtimeEventId);
        return result;
    }
}
