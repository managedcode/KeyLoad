using KeyLoad.Features.InternalSerialization;
using KeyLoad.Replication;
using Orleans.Serialization.Buffers;
using Orleans.Serialization.Codecs;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class ReplicaInspectionResolutionArrayGuardTests
{
    [Test]
    public async Task AcIs005ConfiguredArrayLimitRejectsBeforeReadingOrRecordingAnyRetainedEntry()
    {
        ReplicaEntry[] entries = [new(1, 3, null), new(2, 3, null)];
        var bytes = NativeSerialization.Serialize(entries);
        var rejected = ReadOversizedArray(bytes);
        await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(rejected.FirstEntryUnread).IsTrue();
        await Assert.That(rejected.RecordedArray).IsFalse();
    }

    private static GuardEvidence ReadOversizedArray(byte[] bytes)
    {
        using var session = NativeSerializerProviders.Get(typeof(ReplicaEntry[])).Sessions.GetSession();
        using var bound = ReplicaInspectionBuffers.Bind(session, new(bytes, maximumEntries: 1));
        var reader = Reader.Create(bytes, session);
        NativePayloadHeader.Validate(reader.ReadFieldHeader());
        _ = ReferenceCodec.CreateRecordPlaceholder(session);
        _ = UInt32Codec.ReadValue(ref reader, reader.ReadFieldHeader());
        var arrayField = reader.ReadFieldHeader();
        var codec = new ReplicaEntryArrayInspectionCodec(new ReplicaEntryInspectionCodec());
        try
        {
            _ = codec.ReadValue(ref reader, arrayField);
        }
        catch (KeyLoadException failure)
        {
            var next = reader.ReadFieldHeader();
            return new(failure.Code, next.HasFieldId && next.FieldIdDelta == 1 && !next.IsEndObject,
                session.ReferencedObjects.TryGetReferencedObject(3) is ReplicaEntry[]);
        }
        throw new InvalidOperationException("The genuine bounded codec accepted an oversized generated array.");
    }

    private sealed record GuardEvidence(ErrorCode Code, bool FirstEntryUnread, bool RecordedArray);
}
