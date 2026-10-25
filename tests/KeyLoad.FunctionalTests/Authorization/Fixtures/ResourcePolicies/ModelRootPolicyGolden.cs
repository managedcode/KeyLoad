using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ModelRootPolicyGolden
{
    private const char LeadingPathSeparator = '/';
    internal static string Payload(ModelRootPolicyFixture fixture, bool read)
        => fixture.HeaderRoot ? fixture.SafePayload : read ? fixture.Payload : "{}";
    internal static string Headers(ModelRootPolicyFixture fixture, bool read)
        => !fixture.HeaderRoot ? ModelRootPolicyFixture.SafeHeaders : read ? ModelRootPolicyFixture.Headers : "{}";
    internal static ImmutableArray<string> Omitted(ModelRootPolicyFixture fixture, bool read)
        => read ? [fixture.HeaderRoot ? "/payload/" : "/headers/"]
            : fixture.HeaderRoot ? ["/payload/", "/headers"] : ["/payload", "/headers/"];

    internal static string ModelJson(ModelRootPolicyFixture fixture, bool read)
    {
        var model = new JsonObject();
        if (fixture.Queue)
        {
            model[ModelRootPolicyFixture.IdField] = "message";
            model[ModelRootPolicyFixture.StateField] = "Ready";
            model[ModelRootPolicyFixture.AttemptsField] = 0;
            model[ModelRootPolicyFixture.StateVersionField] = 1L;
            model[ModelRootPolicyFixture.NotBeforeField] = null;
            model[ModelRootPolicyFixture.ExpiresAtField] = null;
        }
        else
        {
            model[ModelRootPolicyFixture.EventIdField] = "event";
            model[ModelRootPolicyFixture.EventTypeField] = "Created";
            model[ModelRootPolicyFixture.SchemaVersionField] = 1;
            model[ModelRootPolicyFixture.RevisionField] = 1L;
            model[ModelRootPolicyFixture.EventSequenceField] = 1L;
            model[ModelRootPolicyFixture.RecordedAtField] = JsonSerializer.SerializeToNode(ModelRootPolicyFixture.Epoch, JsonDefaults.Options);
        }
        if (read || fixture.HeaderRoot)
        {
            model[ModelRootPolicyFixture.PayloadField] = JsonNode.Parse(Payload(fixture, read));
        }
        if (read || !fixture.HeaderRoot)
        {
            model[ModelRootPolicyFixture.HeadersField] = JsonNode.Parse(Headers(fixture, read));
        }
        return model.ToJsonString();
    }

    internal static QueryPage Page(ModelRootPolicyFixture fixture, long cut, bool read, string? json = null, bool join = false,
        ImmutableArray<string>? selectedFields = null)
    {
        var fields = selectedFields ?? Omitted(fixture, read);
        var sources = join ? ImmutableArray.Create(new QueryRowSource("e", fixture.Id, 1L),
            new QueryRowSource("d", ModelRootPolicyFixture.TargetId, 1L)) : (ImmutableArray<QueryRowSource>?)null;
        if (join)
        {
            fields = [.. fields.Select(field => "e." + field.TrimStart(LeadingPathSeparator))];
        }
        return new([new(fixture.Id, 1L, json ?? ModelJson(fixture, read), !fields.IsEmpty, fields, sources)], null, cut,
            join ? fixture.Queue ? "bounded-queue-entity-reference-inner-join" : "bounded-event-entity-reference-inner-join" : fixture.AccessPath);
    }

    internal static ImmutableArray<string> ProtectedOmitted(ModelRootPolicyFixture fixture, bool read)
        => read ? [] : [fixture.HeaderRoot ? "/headers" : "/payload"];

    internal static string Value(ModelRootPolicyFixture fixture, bool read, bool join = false)
        => JsonSerializer.Serialize(join ? new
        {
            secret = read ? fixture.RootValue : null,
            name = "Ada"
        }
            : (object)new
            {
                secret = read ? fixture.RootValue : null
            }, JsonDefaults.Options);

    internal static StreamPage Stream(ModelRootPolicyFixture fixture, long cut, bool read)
    {
        var stream = new StreamRef(fixture.Harness.Partition, ModelRootPolicyFixture.Source, ModelRootPolicyFixture.StreamId, 1L);
        var record = new EventRecord(stream, 1L, 1L, new(fixture.Id, "Created", Payload(fixture, read), Headers(fixture, read)),
            ModelRootPolicyFixture.Epoch);
        return new(stream, new(1L, 1L, 1L), [record], cut, false, null, cut);
    }

    internal static MessageInspection Message(ModelRootPolicyFixture fixture, bool read)
        => new(new(fixture.Id, MessageState.Ready, 0, 1L, 1L, null, null), Payload(fixture, read), Headers(fixture, read));
}
