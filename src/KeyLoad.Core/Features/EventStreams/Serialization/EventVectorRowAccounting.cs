using System.Collections.Immutable;

namespace KeyLoad.Core;

internal static class EventVectorRowAccounting
{
    private const long EmptyBytes = 0;
    private const string InvalidRows = "The event vector owned row inventory is invalid.";

    internal static long PayloadBytes(ImmutableArray<EventVectorEncodedRow> rows)
    {
        if (rows.IsDefault)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidRows); }
        var bytes = EmptyBytes;
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            if (row.Key.IsEmpty || row.Value.IsEmpty || !keys.Add(Convert.ToHexString(row.Key.Span)))
            { throw Errors.Fail(ErrorCode.Corruption, InvalidRows); }
            bytes = checked(bytes + row.EncodedBytes);
        }
        return bytes;
    }

    internal static EventVectorEncodedRow Header(EventVectorMap header,
        ImmutableArray<EventVectorEncodedRow> payloadRows, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(header);
        var payloadBytes = PayloadBytes(payloadRows);
        var ownedHeader = header with { RetainedPayloadBytes = payloadBytes };
        var encoded = EventVectorEncodedRow.Create(EventVectorKeys.Map(header.ControlPartition, header.MapId),
            ownedHeader, admission);
        admission.RequireEncodedBytes(checked(payloadBytes + encoded.EncodedBytes));
        return encoded;
    }
}
