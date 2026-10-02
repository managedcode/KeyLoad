using System.Text.Json;
using KeyLoad.Replication;

namespace KeyLoad.Orleans;

internal static class ReplicaAppendClassification
{
    private enum Field { Leader, Term, PreviousIndex, PreviousTerm, CommittedIndex, Entries }
    private enum EntryField { Index, Term, Operation }
    private static readonly string[] Fields =
    [
        ReplicaTransportProtocol.LeaderIdField, ReplicaPayloadReader.Name(nameof(AppendRequest.Term)),
        ReplicaPayloadReader.Name(nameof(AppendRequest.PreviousIndex)), ReplicaPayloadReader.Name(nameof(AppendRequest.PreviousTerm)),
        ReplicaPayloadReader.Name(nameof(AppendRequest.CommittedIndex)), ReplicaPayloadReader.Name(nameof(AppendRequest.Entries))
    ];
    private static readonly string[] EntryFields =
    [
        ReplicaPayloadReader.Name(nameof(ReplicaEntry.Index)), ReplicaPayloadReader.Name(nameof(ReplicaEntry.Term)),
        ReplicaPayloadReader.Name(nameof(ReplicaEntry.Operation))
    ];

    internal static ReplicaReplayPool Classify(ref Utf8JsonReader reader, ReplicaConfiguration configuration, int maximumControlPayloadBytes)
    {
        ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.StartObject);
        ulong seen = 0;
        var header = new ReplicaAppendHeader();
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var field = (Field)ReplicaPayloadReader.Field(ref reader, Fields, ref seen);
            ReadField(ref reader, field, header, configuration, maximumControlPayloadBytes);
        }

        ReplicaPayloadReader.CompleteObject(ref reader, Fields, seen);
        Validate(header);
        return header.Entries.ControlOnly ? ReplicaReplayPool.Critical : ReplicaReplayPool.DataAppend;
    }

    private static void ReadField(ref Utf8JsonReader reader, Field field, ReplicaAppendHeader header, ReplicaConfiguration configuration,
        int maximumControlPayloadBytes)
    {
        switch (field)
        {
            case Field.Leader:
                ReplicaPayloadReader.Identity(ref reader);
                break;
            case Field.Term:
                header.Term = ReplicaPayloadReader.Number(ref reader, positive: true);
                break;
            case Field.PreviousIndex:
                header.PreviousIndex = ReplicaPayloadReader.Number(ref reader);
                break;
            case Field.PreviousTerm:
                header.PreviousTerm = ReplicaPayloadReader.Number(ref reader);
                break;
            case Field.CommittedIndex:
                ReplicaPayloadReader.Number(ref reader);
                break;
            case Field.Entries:
                var start = reader.TokenStartIndex;
                header.Entries = Entries(ref reader, configuration.MaxAppendEntries, maximumControlPayloadBytes);
                ReplicaPayloadReader.Require(reader.BytesConsumed - start <= configuration.MaxAppendBytes);
                break;
        }
    }

    private static ReplicaClassifiedEntries Entries(ref Utf8JsonReader reader, int maximum, int maximumControlPayloadBytes)
    {
        ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.StartArray);
        var result = new ReplicaClassifiedEntries(0, 0, 0, 0, true);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ReplicaPayloadReader.Require(result.Count < maximum);
            var entry = Entry(ref reader, maximumControlPayloadBytes);
            if (result.Count == 0)
            {
                result = result with { FirstIndex = entry.Index, FirstTerm = entry.Term };
            }

            ReplicaPayloadReader.Require(result.FirstIndex <= long.MaxValue - result.Count
                && entry.Index == result.FirstIndex + result.Count && entry.Term >= result.LastTerm);
            result = result with { Count = result.Count + 1, LastTerm = entry.Term, ControlOnly = result.ControlOnly && entry.Control };
        }

        ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.EndArray);
        return result;
    }

    private static (long Index, long Term, bool Control) Entry(ref Utf8JsonReader reader, int maximumControlPayloadBytes)
    {
        ReplicaPayloadReader.Require(reader.TokenType == JsonTokenType.StartObject);
        ulong seen = 0;
        long index = 0;
        long term = 0;
        var control = true;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var field = (EntryField)ReplicaPayloadReader.Field(ref reader, EntryFields, ref seen);
            switch (field)
            {
                case EntryField.Index:
                    index = ReplicaPayloadReader.Number(ref reader, positive: true);
                    break;
                case EntryField.Term:
                    term = ReplicaPayloadReader.Number(ref reader, positive: true);
                    break;
                case EntryField.Operation:
                    if (reader.TokenType != JsonTokenType.Null)
                    {
                        control = ReplicaOperationClassification.IsControl(ref reader, maximumControlPayloadBytes);
                    }

                    break;
            }
        }

        ReplicaPayloadReader.CompleteObject(ref reader, EntryFields, seen);
        return (index, term, control);
    }

    private static void Validate(ReplicaAppendHeader header)
    {
        ReplicaPayloadReader.Require(header.PreviousTerm <= header.Term && (header.PreviousIndex == 0) == (header.PreviousTerm == 0));
        if (header.Entries.Count > 0)
        {
            ReplicaPayloadReader.Require(header.PreviousIndex <= long.MaxValue - header.Entries.Count
                && header.Entries.FirstIndex == header.PreviousIndex + 1 && header.Entries.FirstTerm >= header.PreviousTerm
                && header.Entries.LastTerm <= header.Term);
        }
    }
}

internal sealed class ReplicaAppendHeader
{
    internal long Term { get; set; }
    internal long PreviousIndex { get; set; }
    internal long PreviousTerm { get; set; }
    internal ReplicaClassifiedEntries Entries { get; set; }
}

internal readonly record struct ReplicaClassifiedEntries(int Count, long FirstIndex, long FirstTerm, long LastTerm, bool ControlOnly);
