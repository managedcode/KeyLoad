using Dom = Microsoft.SqlServer.TransactSql.ScriptDom;

namespace KeyLoad.QueryExecution;

internal sealed class ScriptDomCheckedTokens(IList<Dom.TSqlParserToken> tokens, global::Microsoft.Extensions.Options.IOptions<DatabaseLimits> limitsOptions, Action check)
    : IList<Dom.TSqlParserToken>
{
    internal int MaximumDepth => limitsOptions.Value.MaxQueryDepth;
    internal int MaximumNodes => limitsOptions.Value.MaxQueryTokens;
    public Dom.TSqlParserToken this[int index]
    {
        get
        {
            check();
            var token = tokens[index];
            ScriptDomParsingObservation.Token(index);
            check();
            return token;
        }
        set => throw new NotSupportedException();
    }
    public int Count
    {
        get
        {
            check();
            return tokens.Count;
        }
    }
    public bool IsReadOnly => true;
    public IEnumerator<Dom.TSqlParserToken> GetEnumerator()
    {
        for (var index = FirstIndex; index < Count; index++)
        {
            yield return this[index];
        }
    }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(Dom.TSqlParserToken item)
    {
        check();
        return tokens.IndexOf(item);
    }
    public bool Contains(Dom.TSqlParserToken item) => IndexOf(item) >= FirstIndex;
    public void CopyTo(Dom.TSqlParserToken[] array, int arrayIndex)
    {
        foreach (var token in this)
        {
            array[arrayIndex++] = token;
        }
    }
    public void Add(Dom.TSqlParserToken item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, Dom.TSqlParserToken item) => throw new NotSupportedException();
    public bool Remove(Dom.TSqlParserToken item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();

    private const int FirstIndex = 0;
}
