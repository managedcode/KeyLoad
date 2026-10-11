using System.Security.Cryptography;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorRetentionPins
{
    private const int Version = 1;
    private const long FirstRevision = 1;
    private const long BeforeFirstPosition = 0;
    private const string InvalidPin = "The native event vector retention pin is inconsistent.";
    private const string PinnedHistory = "The retained history is protected by an original event vector source pin.";
    private const string ScanExhausted = "The event vector retention pin scan budget is exhausted.";

    internal static void Require(IKeyValueView view, EventSourceRef source, long tail, long cut,
        IOptions<DatabaseLimits> options, StorageReadObserver observer)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(observer);
        options.Value.Validate();
        var admission = new EventVectorAdmissionPolicy(options);
        var scan = view.VisitRange(EventVectorKeys.Pins(source.Partition), options.Value.MaxScanRecords,
            (key, value) =>
            {
                admission.RequireEncodedBytes(checked((long)key.Length + value.Length));
                var pin = NativeSerialization.Deserialize<EventVectorSourcePin>(value);
                RequireIdentity(pin, key, source.Partition);
                if (pin.Source == source)
                {
                    if (pin.Position > tail)
                    { throw Errors.Fail(ErrorCode.Corruption, InvalidPin); }
                    if (cut > pin.Position)
                    { throw Errors.Fail(ErrorCode.ResourceExhausted, PinnedHistory); }
                }
                return true;
            }, observer: observer);
        if (scan.HasMore)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, ScanExhausted); }
    }

    private static void RequireIdentity(EventVectorSourcePin pin, ReadOnlySpan<byte> key, PartitionRef partition)
    {
        if (pin.Version != Version || pin.MapId == Guid.Empty || pin.ControlIncarnation == Guid.Empty
            || pin.Source.Partition != partition || pin.OriginalPinCommandId == Guid.Empty
            || pin.PinRevision < FirstRevision || pin.Position < BeforeFirstPosition
            || pin.OriginalPinBodyDigest.Length != SHA256.HashSizeInBytes
            || !key.SequenceEqual(EventVectorKeys.Pin(pin.Source, pin.MapId)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidPin); }
        DatabaseEngine.ValidatePartition(pin.ControlPartition);
        DatabaseEngine.ValidatePartition(pin.Source.Partition);
    }
}
