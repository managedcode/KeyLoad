using System.Collections.Immutable;
using System.Text.Json;

namespace KeyLoad.ClusterTests.DatabaseComposition;

/// <summary>Fresh public requests preserve values and array order while rebuilding every Analytics map.</summary>
internal static class FunctionalGateAnalyticsIdentity
{
    private const double MetricChange = 1;
    private const long RevisionChange = 1;

    internal static CommandRequest Rebuild(CommandRequest command)
        => command with
        {
            Mutations = command.Mutations.Select(Rebuild).ToImmutableArray()
        };

    internal static CommandRequest Changed(CommandRequest command)
    {
        var mutations = Rebuild(command).Mutations.ToBuilder();
        for (var index = 0; index < mutations.Count; index++)
        {
            if (mutations[index] is UpsertAnalyticsEvents upsert)
            {
                var events = upsert.Events.ToBuilder();
                var first = events[0];
                var metric = first.Metrics.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
                events[0] = first with
                {
                    Metrics = first.Metrics.SetItem(metric.Key, metric.Value + MetricChange)
                };
                mutations[index] = upsert with
                {
                    Events = events.ToImmutable()
                };
                return command with
                {
                    Mutations = mutations.ToImmutable()
                };
            }
            if (mutations[index] is DeleteAnalyticsEvents delete)
            {
                var revision = delete.ExpectedRevisions.OrderBy(pair => pair.Key, StringComparer.Ordinal).First();
                mutations[index] = delete with
                {
                    ExpectedRevisions = delete.ExpectedRevisions.SetItem(revision.Key, checked(revision.Value + RevisionChange))
                };
                return command with
                {
                    Mutations = mutations.ToImmutable()
                };
            }
        }
        throw new InvalidOperationException("Analytics identity fixture requires an Analytics mutation.");
    }

    internal static JsonElement Wire(CommandRequest command)
    {
        using var output = new MemoryStream();
        using (var writer = new Utf8JsonWriter(output))
        {
            Write(writer, JsonSerializer.SerializeToElement(Rebuild(command), JsonDefaults.Options));
        }
        using var document = JsonDocument.Parse(output.ToArray());
        return document.RootElement.Clone();
    }

    private static Mutation Rebuild(Mutation mutation)
        => mutation switch
        {
            UpsertAnalyticsEvents upsert => upsert with
            {
                Events = upsert.Events.Select(value => value with
                {
                    Dimensions = Reverse(value.Dimensions),
                    Metrics = Reverse(value.Metrics)
                }).ToImmutableArray(),
                ExpectedRevisions = Reverse(upsert.ExpectedRevisions)
            },
            DeleteAnalyticsEvents delete => delete with { ExpectedRevisions = Reverse(delete.ExpectedRevisions) },
            _ => mutation
        };

    private static ImmutableDictionary<string, T> Reverse<T>(ImmutableDictionary<string, T> values)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, T>(StringComparer.Ordinal);
        foreach (var pair in values.OrderByDescending(pair => pair.Key, StringComparer.Ordinal))
        {
            builder.Add(pair.Key, pair.Value);
        }

        return builder.ToImmutable();
    }

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            var properties = value.EnumerateObject().ToArray();
            foreach (var property in properties.OrderBy(property => property.Name == "kind" ? 0 : 1)
                .ThenByDescending(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                Write(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray())
            {
                Write(writer, item);
            }

            writer.WriteEndArray();
        }
        else
        {
            value.WriteTo(writer);
        }
    }
}
