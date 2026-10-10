using System.Globalization;
using KeyLoad.Core;

namespace KeyLoad.Query.Features.QueryExecution;

internal sealed class ModelSqlSourceParser(SqlTokenCursor cursor)
{
    private const long FirstGeneration = 1L;

    internal (string Collection, ModelQuerySource? Source) Read()
    {
        if (!cursor.Current.Quoted && cursor.Is(SqlSyntax.Events))
        { return Events(); }
        if (!cursor.Current.Quoted && cursor.Is(SqlSyntax.QueueMessages))
        { return Queue(); }
        if (!cursor.Current.Quoted && cursor.Is(SqlSyntax.TopicEvents))
        { return Topic(); }
        return (cursor.Identifier(), null);
    }

    private (string Collection, ModelQuerySource? Source) Events()
    {
        var collection = cursor.Current.Text;
        cursor.Advance();
        if (!cursor.Eat(SqlSyntax.OpenParen))
        { return (collection, null); }
        var streamSet = StringArgument();
        cursor.Need(SqlSyntax.Comma);
        var streamId = StringArgument();
        var generation = Generation();
        cursor.Need(SqlSyntax.CloseParen);
        return (streamSet, new(ModelQuerySourceKind.Events, streamId, generation));
    }

    private (string Collection, ModelQuerySource? Source) Queue()
    {
        var collection = cursor.Current.Text;
        cursor.Advance();
        if (!cursor.Eat(SqlSyntax.OpenParen))
        { return (collection, null); }
        var queue = StringArgument();
        cursor.Need(SqlSyntax.CloseParen);
        return (queue, new(ModelQuerySourceKind.QueueMessages, queue));
    }

    private (string Collection, ModelQuerySource? Source) Topic()
    {
        var collection = cursor.Current.Text;
        cursor.Advance();
        if (!cursor.Eat(SqlSyntax.OpenParen))
        { return (collection, null); }
        var topic = StringArgument();
        var generation = Generation();
        cursor.Need(SqlSyntax.CloseParen);
        return (topic, new(ModelQuerySourceKind.TopicEvents, topic, generation));
    }

    private long Generation()
    {
        var generation = FirstGeneration;
        if (cursor.Eat(SqlSyntax.Comma))
        {
            if (cursor.Current.Kind != SqlTokenKind.Number
                || !long.TryParse(cursor.Current.Text, NumberStyles.None, CultureInfo.InvariantCulture, out generation)
                || generation < FirstGeneration)
            { throw SqlSyntax.Invalid(); }
            cursor.Advance();
        }
        return generation;
    }

    private string StringArgument()
    {
        if (cursor.Current.Kind != SqlTokenKind.String)
        { throw SqlSyntax.Invalid(); }
        var value = cursor.Current.Text;
        cursor.Advance();
        JsonData.Identifier(value);
        return value;
    }
}
