namespace KeyLoad.Core.Features.Messaging;

internal static class QueueOrderProtocol
{
    internal const string Space = "queue-order";
    internal const string Alias = "keyload.core.queue-order-reference.v1";
    internal const int DomainKeyComponent = 3;
    internal const int PartitionKeyComponent = 4;
    internal const int ResourceKeyComponent = 5;
    internal const int MessageKeyComponent = 6;
    internal const int MessageKeyComponents = 7;
    internal const string IncompleteAbsence = "The bounded queue scan cannot prove delivery input absence.";
    internal const int Initial = 0;
    internal const int Step = 1;
    internal const int MessageIdField = 0;
    internal const int SequenceField = 1;
    internal const int GenerationField = 2;
    internal const string MissingKey = "Strict queue ordering requires an ordering key.";
    internal const string MissingAuthority = "The strict queue order authority is unavailable.";
    internal const string InvalidAuthority = "The strict queue order authority does not match its message.";
    internal const string NonemptyChange = "The ordering profile cannot change while queue input is retained.";
}

[global::Orleans.GenerateSerializer, global::Orleans.Alias(QueueOrderProtocol.Alias)]
internal sealed record QueueOrderReference(
    [property: global::Orleans.Id(QueueOrderProtocol.MessageIdField)] string MessageId,
    [property: global::Orleans.Id(QueueOrderProtocol.SequenceField)] long ActiveOrderSequence,
    [property: global::Orleans.Id(QueueOrderProtocol.GenerationField)] long DeliveryGeneration);
