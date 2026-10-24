
namespace KeyLoad.QueryExecution;

internal sealed class ScriptDomBoundedReader(string text, Action check) : TextReader
{
    private int position;
    public override int Peek()
    {
        check();
        return position < text.Length ? text[position] : EndOfInput;
    }
    public override int Read()
    {
        check();
        return position < text.Length ? text[position++] : EndOfInput;
    }
    public override int Read(char[] buffer, int index, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return Read(buffer.AsSpan(index, count));
    }
    public override int Read(Span<char> buffer)
    {
        check();
        var count = Math.Min(buffer.Length, text.Length - position);
        text.AsSpan(position, count).CopyTo(buffer);
        position += count;
        check();
        return count;
    }
    private const int EndOfInput = -1;
}
