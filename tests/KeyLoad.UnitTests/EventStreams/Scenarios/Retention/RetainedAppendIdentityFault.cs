using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal sealed class RetainedAppendIdentityFault(RetainedAppendIdentityFixture fixture, string kind)
{
    private readonly byte[] idKey = KeySpace.Partition("event-id", fixture.Harness.Partition,
        RetainedAppendIdentityFixture.Streams, RetainedAppendIdentityFixture.Target, 1L, RetainedAppendIdentityFixture.Candidate);
    private readonly byte[] eventKey = KeySpace.Partition("event", fixture.Harness.Partition,
        RetainedAppendIdentityFixture.Streams, RetainedAppendIdentityFixture.Target, 1L, 1L);
    private byte[] original = [];
    internal void Damage(ZoneTreeStore store)
    {
        if (store.Read(view => view.ReadOwnedValue(idKey)) is not null)
        {
            throw new InvalidOperationException("The candidate ID must be absent in the selected stream.");
        }
        original = store.Read(view => view.ReadOwnedValue(eventKey))
            ?? throw new InvalidOperationException("The literal original event is missing.");
        var record = OrleansSerialization.Deserialize<EventRecord>(original);
        var identity = new EventIdentity(kind == "foreign-locator" ? RetainedAppendIdentityFixture.Foreign
            : RetainedAppendIdentityFixture.Target, 1, 1);
        var damaged = SelectRecord(record);
        store.Commit((tx, _) =>
        {
            if (damaged is not null)
            {
                tx.PutRecord(eventKey, damaged);
            }
            tx.PutRecord(idKey, identity);
            return true;
        });
    }
    private EventRecord? SelectRecord(EventRecord record) => kind switch
    {
        "foreign-locator" or "event-id" => null,
        "foreign-record" => record with
        {
            Stream = fixture.Stream with
            {
                StreamId = RetainedAppendIdentityFixture.Foreign
            },
            Data = RetainedAppendIdentityFixture.Data(RetainedAppendIdentityFixture.Candidate)
        },
        "record-revision" => record with
        {
            Revision = 99,
            Data = RetainedAppendIdentityFixture.Data(RetainedAppendIdentityFixture.Candidate)
        },
        _ => throw new InvalidOperationException("The retained identity fault selection is invalid.")
    };

    internal void Repair(ZoneTreeStore store)
    {
        if (original.Length == 0)
        {
            throw new InvalidOperationException("The original repair bytes are unavailable.");
        }
        store.Commit((tx, _) =>
        {
            tx.Delete(idKey);
            tx.Put(eventKey, original);
            return true;
        });
    }
}
