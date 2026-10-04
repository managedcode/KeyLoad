using System.Text.Json;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string FromJsonProperty = "from";
    private const string ToJsonProperty = "to";
    private const string LabelJsonProperty = "label";
    private const string AttributesJsonProperty = "attributesJson";
    private const string PartitionJsonProperty = "partition";
    private const string CollectionJsonProperty = "collection";
    private const string IdJsonProperty = "id";
    private const string TenantJsonProperty = "tenantId";
    private const string DatabaseJsonProperty = "databaseId";
    private const string DomainJsonProperty = "transactionDomainId";
    private const string PartitionKeyJsonProperty = "partitionKey";
    private const string AtomicPartitionIdJsonProperty = "atomicPartitionId";

    private List<Mutation> ExpandQueueToGraph(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, QueueToGraph command, DateTimeOffset now, ReadExecutionBudget budget)
    {
        AuthorizeComposition(tx, principal, partition, command);
        var lane = new QueueLaneRef(partition, command.Queue);
        var ids = new List<string>();
        var result = budget.VisitRange(tx, QueueKey(ReadyQueueSpace, lane), command.MaxMessages, (_, value) =>
        {
            ids.Add(NativeSerialization.Deserialize<string>(value));
            return true;
        });
        if (result.HasMore)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, CompositionLimit);
        }
        var effects = new List<Mutation>(ids.Count);
        foreach (var id in ids)
        {
            var metadata = budget.ReadRecord<MessageMetadata>(tx, QueueKey(MessageMetadataSpace, lane, id))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyMessage);
            if (metadata.Id != id || metadata.State != MessageState.Ready)
            {
                throw Errors.Fail(ErrorCode.Corruption, MissingReadyMessage);
            }
            if (metadata.ExpiresAt <= now || metadata.NotBefore > now)
            {
                continue;
            }
            var body = budget.ReadRecord<MessageBody>(tx, QueueKey(MessageBodySpace, lane, id))
                ?? throw Errors.Fail(ErrorCode.Corruption, MissingReadyMessage);
            if (body.Id != id)
            {
                throw Errors.Fail(ErrorCode.Corruption, MissingReadyMessage);
            }
            var link = ReadLink(body.PayloadJson, partition);
            VisibleVertex(tx, principal, link.From, budget);
            VisibleVertex(tx, principal, link.To, budget);
            var edgeId = command.EdgeIdPrefix + id;
            JsonData.Identifier(edgeId);
            effects.Add(new UpsertEdge(command.Graph, edgeId, link.From, link.To, link.Label,
                link.AttributesJson, ExpectedRevision: 0));
        }
        return effects;
    }

    private List<Mutation> ExpandGraphToQueue(IAtomicTransaction tx, PrincipalRecord principal,
        PartitionRef partition, GraphToQueueMutation command, ReadExecutionBudget budget)
    {
        AuthorizeComposition(tx, principal, partition, command);
        var resource = Resource(tx, partition, command.Graph, ResourceKind.Graph);
        var traversal = new GraphTraversalReader(this, tx, principal, resource, partition, command.Graph,
            command.Start, command.MaxDepth, command.MaxVertices, command.MaxEdges, null, budget).Read();
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

    private QueueGraphLink ReadLink(string json, PartitionRef partition)
    {
        try
        {
            using var parsed = JsonDocument.Parse(json);
            if (parsed.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidLink);
            }
            if (!HasExactProperties(parsed.RootElement, [FromJsonProperty, ToJsonProperty, LabelJsonProperty], [AttributesJsonProperty])
                || !ValidEntityJson(parsed.RootElement.GetProperty(FromJsonProperty))
                || !ValidEntityJson(parsed.RootElement.GetProperty(ToJsonProperty)))
            {
                throw Errors.Fail(ErrorCode.Validation, InvalidLink);
            }
            var link = JsonSerializer.Deserialize<QueueGraphLink>(json, JsonDefaults.Options)
                ?? throw Errors.Fail(ErrorCode.Validation, InvalidLink);
            ValidateLink(link, partition);
            ValidatePartitionDigest(parsed.RootElement.GetProperty(FromJsonProperty), link.From.Partition);
            ValidatePartitionDigest(parsed.RootElement.GetProperty(ToJsonProperty), link.To.Partition);
            return link;
        }
        catch (JsonException)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLink);
        }
    }

    private static bool ValidEntityJson(JsonElement element)
    {
        if (!HasExactProperties(element, [PartitionJsonProperty, CollectionJsonProperty, IdJsonProperty], []))
        {
            return false;
        }
        return HasExactProperties(element.GetProperty(PartitionJsonProperty),
            [TenantJsonProperty, DatabaseJsonProperty, DomainJsonProperty, PartitionKeyJsonProperty],
            [AtomicPartitionIdJsonProperty]);
    }

    private static void ValidatePartitionDigest(JsonElement entity, PartitionRef partition)
    {
        var encoded = entity.GetProperty(PartitionJsonProperty);
        if (encoded.TryGetProperty(AtomicPartitionIdJsonProperty, out var digest)
            && (digest.ValueKind != JsonValueKind.String || digest.GetString() != partition.AtomicPartitionId))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLink);
        }
    }

    private static bool HasExactProperties(JsonElement element, string[] required, string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if ((!required.Contains(property.Name, StringComparer.Ordinal)
                && !optional.Contains(property.Name, StringComparer.Ordinal)) || !seen.Add(property.Name))
            {
                return false;
            }
        }
        return required.All(seen.Contains);
    }

    private void ValidateLink(QueueGraphLink link, PartitionRef partition)
    {
        if (link.From is null || link.To is null || link.From.Partition != partition || link.To.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidLink);
        }
        JsonData.Identifier(link.From.Collection);
        JsonData.Identifier(link.From.Id);
        JsonData.Identifier(link.To.Collection);
        JsonData.Identifier(link.To.Id);
        JsonData.Identifier(link.Label);
        JsonData.Validate(link.AttributesJson, Limits);
    }
}
