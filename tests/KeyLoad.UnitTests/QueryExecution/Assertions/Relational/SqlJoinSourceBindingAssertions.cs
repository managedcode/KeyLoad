namespace KeyLoad.UnitTests.QueryExecution;

internal static class SqlJoinSourceBindingAssertions
{
    private const string Ada = "{\"customer_name\":\"Ada\"}";
    private const string Max = """{"customer_name":"Max"}""";
    private const string Lin = "{\"customer_name\":\"Lin\"}";
    private const string Order = "order";
    private const string Customer = "customer";
    private const string FirstMessage = "a";
    private const string LastMessage = "c";
    private const string FirstEvent = "z";
    private const string LastEvent = "m";
    private const string FirstTarget = "c1";
    private const string LastTarget = "c2";
    private const string TypedLeft = "l";
    private const string TypedRight = "r";
    private const string QueueLeft = "q";
    private const string EventLeft = "e";
    private const string ModelRight = "d";
    private const long InitialRevision = 1;
    private const string RepairedMessage = "b";
    private const string RepairedEvent = "a";
    private const string RepairedTarget = "m";
    private const long SecondEventRevision = 2;
    private const int RepairIndex = 1;
    private const long ThirdEventRevision = 3;
    private const string TypedPath = "bounded-primary-key-inner-join";
    private const string QueuePath = "bounded-queue-entity-reference-inner-join";
    private const string EventPath = "bounded-event-entity-reference-inner-join";

    internal static async Task PageAsync(global::KeyLoad.QueryPage page, string kind, long position, bool repaired = false)
    {
        global::KeyLoad.QueryRow[] rows = kind switch
        {
            SqlJoinSourceBindingFixture.Typed => [new(Order, InitialRevision, Ada, false, [], [new(TypedLeft, Order, InitialRevision), new(TypedRight, Customer, InitialRevision)])],
            SqlJoinSourceBindingFixture.Queue => [new(FirstMessage, InitialRevision, Ada, false, [], [new(QueueLeft, FirstMessage, InitialRevision), new(ModelRight, FirstTarget, InitialRevision)]),
                new(LastMessage, InitialRevision, Lin, false, [], [new(QueueLeft, LastMessage, InitialRevision), new(ModelRight, LastTarget, InitialRevision)])],
            _ => [new(FirstEvent, InitialRevision, Ada, false, [], [new(EventLeft, FirstEvent, InitialRevision), new(ModelRight, FirstTarget, InitialRevision)]),
                new(LastEvent, ThirdEventRevision, Lin, false, [], [new(EventLeft, LastEvent, ThirdEventRevision), new(ModelRight, LastTarget, InitialRevision)])]
        };
        var literalRows = rows.ToList();
        if (repaired && kind != SqlJoinSourceBindingFixture.Typed)
        {
            var events = kind == SqlJoinSourceBindingFixture.Events;
            var id = events ? RepairedEvent : RepairedMessage;
            var revision = events ? SecondEventRevision : InitialRevision;
            literalRows.Insert(RepairIndex, new(id, revision, Max, false, [],
                [new(events ? EventLeft : QueueLeft, id, revision), new(ModelRight, RepairedTarget, InitialRevision)]));
        }
        var expected = new global::KeyLoad.QueryPage([.. literalRows], null, position,
            kind == SqlJoinSourceBindingFixture.Typed ? TypedPath : kind == SqlJoinSourceBindingFixture.Queue ? QueuePath : EventPath);
        await Assert.That(global::KeyLoad.JsonDefaults.Serialize(page).AsSpan().SequenceEqual(global::KeyLoad.JsonDefaults.Serialize(expected))).IsTrue();
    }
}
