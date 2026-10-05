using System.Text;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ReadExecutionBudgetGrantSeed
{
    private const string FirstKeyText = "grant/a";
    private const string SecondKeyText = "grant/b";
    private const string EmptyRangeText = "missing/";
    private const string MissingKeyText = "grant/missing";
    private const string FirstValueText = "first-value";
    private const string SecondValueText = "second-value";

    internal static readonly byte[] Prefix = Encoding.UTF8.GetBytes("grant/");
    internal static readonly byte[] FirstKey = Encoding.UTF8.GetBytes(FirstKeyText);
    internal static readonly byte[] SecondKey = Encoding.UTF8.GetBytes(SecondKeyText);
    internal static readonly byte[] EmptyRange = Encoding.UTF8.GetBytes(EmptyRangeText);
    internal static readonly byte[] MissingKey = Encoding.UTF8.GetBytes(MissingKeyText);
    internal static readonly byte[] FirstValue = Encoding.UTF8.GetBytes(FirstValueText);
    internal static readonly byte[] SecondValue = Encoding.UTF8.GetBytes(SecondValueText);

    internal static TestDatabase CreateDatabase(DatabaseLimits? limits = null)
    {
        var database = new TestDatabase(limits);
        database.Store.Commit((transaction, _) =>
        {
            transaction.Put(FirstKey, FirstValue);
            transaction.Put(SecondKey, SecondValue);
            return true;
        });
        return database;
    }
}
