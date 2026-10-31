using System.Text.Json;
using KeyLoad.Core.GraphTraversal;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCompositionExpansion operations against the caller's database context.</summary>
public static class DatabaseCompositionExpansion
{
    internal const string FromJsonProperty = "from";
    internal const string ToJsonProperty = "to";
    internal const string LabelJsonProperty = "label";
    internal const string AttributesJsonProperty = "attributesJson";
    internal const string PartitionJsonProperty = "partition";
    internal const string CollectionJsonProperty = "collection";
    internal const string IdJsonProperty = "id";
    internal const string TenantJsonProperty = "tenantId";
    internal const string DatabaseJsonProperty = "databaseId";
    internal const string DomainJsonProperty = "transactionDomainId";
    internal const string PartitionKeyJsonProperty = "partitionKey";
    internal const string AtomicPartitionIdJsonProperty = "atomicPartitionId";
    internal const string LifetimeJsonProperty = "lifetime";
    internal const string IncarnationJsonProperty = "incarnation";
    internal static List<Mutation> ExpandQueueToGraphSelection(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, QueueToGraph command, DateTimeOffset now, ReadExecutionBudget budget, IKeyValueView authorityView)
    {
        const int ExpectedRevisionEmptyCount = 0;
        var typedPrimaryKeys = (engine.AuthorizeComposition(authorityView, principal, partition, command)).Value;
        var lane = new QueueLaneRef(partition, command.Queue);
        var ids = new List<(string Id, byte[] ReadyKey)>();
        var result = budget.VisitRange(tx, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.ReadyQueueSpace, lane), command.MaxMessages, (key, value) =>
        {
            ids.Add((OrleansSerialization.Deserialize<string>(value), key.ToArray()));
            return true;
        });
        if (result.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, DatabaseCompositionExecution.CompositionLimit);
        }

        var effects = new List<Mutation>(ids.Count);
        foreach (var (id, readyKey) in ids)
        {
            var metadata = budget.ReadRecord<MessageMetadata>(tx, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageMetadataSpace, lane, id)) ?? throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionExecution.MissingReadyMessage);
            if (metadata.Id != id || metadata.State != MessageState.Ready)
            {
                throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionExecution.MissingReadyMessage);
            }

            if (metadata.ExpiresAt <= now || metadata.NotBefore > now)
            {
                continue;
            }

            var body = budget.ReadRecord<MessageBody>(tx, MessagingExecutionMessaging.QueueKey(QueueSweepTransitions.MessageBodySpace, lane, id)) ?? throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionExecution.MissingReadyMessage);
            if (body.Id != id)
            {
                throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionExecution.MissingReadyMessage);
            }

            MessagingValidation.RequireQueueReadyAuthority(lane, id, readyKey, metadata, body);
            var link = DatabaseCompositionExpansion.ReadLink(body.PayloadJson, partition);
            if (command.FromCollection is not null)
            {
                var selected = (engine.SelectTypedEndpointPair(tx, principal, command, link, budget, typedPrimaryKeys!.Value.FromPrimaryKey, typedPrimaryKeys.Value.ToPrimaryKey)).Value;
                if (selected is null)
                {
                    continue;
                }

                link = selected;
            }
            else
            {
                link = engine.BindVisibleCompositionEndpoints(tx, principal, link, budget);
            }

            var edgeId = command.EdgeIdPrefix + id;
            JsonData.Identifier(edgeId);
            effects.Add(new UpsertEdge(command.Graph, edgeId, link.From, link.To, link.Label, link.AttributesJson, ExpectedRevision: ExpectedRevisionEmptyCount));
        }

        return effects;
    }

    internal static QueueGraphLink BindVisibleCompositionEndpoints(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, QueueGraphLink link, ReadExecutionBudget budget)
    {
        var chargedEndpoints = budget.CreateView(view);
        var from = engine.VisibleVertex(chargedEndpoints, principal, link.From);
        var to = engine.VisibleVertex(chargedEndpoints, principal, link.To);
        // Keep the opaque queue payload unchanged; only the authorized derived effect is bound.
        return link with
        {
            From = from.Reference,
            To = to.Reference
        };
    }

    internal static List<Mutation> ExpandGraphToQueue(this DatabaseEngine engine, IAtomicTransaction tx, PrincipalRecord principal, PartitionRef partition, GraphToQueueMutation command, ReadExecutionBudget budget)
    {
        _ = (engine.AuthorizeComposition(tx, principal, partition, command)).Value;
        var resource = engine.Resource(tx, partition, command.Graph, ResourceKind.Graph);
        var start = engine.VisibleVertex(budget.CreateView(tx), principal, command.Start).Reference;
        var traversal = new GraphTraversalReader(engine, tx, principal, resource, partition, command.Graph, start, command.MaxDepth, command.MaxVertices, command.MaxEdges, null, budget).Read();
        var effects = new List<Mutation>(traversal.Edges.Length);
        foreach (var edge in traversal.Edges)
        {
            var messageId = command.MessageIdPrefix + edge.Id;
            JsonData.Identifier(messageId);
            var link = new QueueGraphLink(edge.From, edge.To, edge.Label, edge.AttributesJson);
            effects.Add(new EnqueueMessage(command.Queue, messageId, JsonSerializer.Serialize(link, JsonDefaults.Options)));
        }

        return effects;
    }

    internal static QueueGraphLink ReadLink(string json, PartitionRef partition)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Errors.Fail(ErrorCode.Validation, DatabaseCompositionExecution.InvalidLink);
            }

            if (!HasExactProperties(parsed.RootElement, [FromJsonProperty, ToJsonProperty, LabelJsonProperty], [AttributesJsonProperty]) || !ValidEntityJson(parsed.RootElement.GetProperty(FromJsonProperty)) || !ValidEntityJson(parsed.RootElement.GetProperty(ToJsonProperty)))
            {
                throw Errors.Fail(ErrorCode.Validation, DatabaseCompositionExecution.InvalidLink);
            }

            var link = JsonSerializer.Deserialize<QueueGraphLink>(json, JsonDefaults.Options) ?? throw Errors.Fail(ErrorCode.Validation, DatabaseCompositionExecution.InvalidLink);
            DatabaseCompositionExpansion.ValidateLink(link, partition);
            ValidatePartitionDigest(parsed.RootElement.GetProperty(FromJsonProperty), link.From.Partition);
            ValidatePartitionDigest(parsed.RootElement.GetProperty(ToJsonProperty), link.To.Partition);
            return link;
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, DatabaseCompositionExecution.InvalidLink);
        }
    }

    internal static bool ValidEntityJson(JsonElement element)
    {
        if (!HasExactProperties(element, [PartitionJsonProperty, CollectionJsonProperty, IdJsonProperty], [LifetimeJsonProperty]))
        {
            return false;
        }

        if (element.TryGetProperty(LifetimeJsonProperty, out var lifetime) && (!HasExactProperties(lifetime, [IncarnationJsonProperty], []) || lifetime.GetProperty(IncarnationJsonProperty).ValueKind != JsonValueKind.String || !KeyLoad.Core.ResourceExecution.ResourceLifetimeIdentity.IsIncarnation(lifetime.GetProperty(IncarnationJsonProperty).GetString())))
        {
            return false;
        }

        return HasExactProperties(element.GetProperty(PartitionJsonProperty), [TenantJsonProperty, DatabaseJsonProperty, DomainJsonProperty, PartitionKeyJsonProperty], [AtomicPartitionIdJsonProperty]);
    }

    internal static void ValidatePartitionDigest(JsonElement entity, PartitionRef partition)
    {
        var encoded = entity.GetProperty(PartitionJsonProperty);
        if (encoded.TryGetProperty(AtomicPartitionIdJsonProperty, out var digest) && (digest.ValueKind != JsonValueKind.String || digest.GetString() != partition.AtomicPartitionId))
        {
            throw Errors.Fail(ErrorCode.Validation, DatabaseCompositionExecution.InvalidLink);
        }
    }

    internal static bool HasExactProperties(JsonElement element, string[] required, string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if ((!required.Contains(property.Name, StringComparer.Ordinal) && !optional.Contains(property.Name, StringComparer.Ordinal)) || !seen.Add(property.Name))
            {
                return false;
            }
        }

        return required.All(seen.Contains);
    }

    internal static void ValidateLink(QueueGraphLink link, PartitionRef partition)
    {
        if (link.From is null || link.To is null || link.From.Partition != partition || link.To.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Validation, DatabaseCompositionExecution.InvalidLink);
        }

        JsonData.Identifier(link.From.Collection);
        JsonData.Identifier(link.From.Id);
        JsonData.Identifier(link.To.Collection);
        JsonData.Identifier(link.To.Id);
        JsonData.Identifier(link.Label);
        JsonData.Validate(link.AttributesJson);
    }
}
