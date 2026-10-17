using System.Text.Json;
using KeyLoad.Query;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeJsonElementTests
{
    private const string NegativeZero = "-0";
    private const string DecimalZeros = "1.00";
    private const string Exponent = "1e+00";
    private const string BeyondDouble = "9007199254740993";
    private const string BeyondDecimal = "1234567890123456789012345678901234567890";
    private const string TinyExponent = "1e-1000";
    private const string OrderedDuplicates = "{\"same\":1.00,\"same\":1e+00,\"\":{\"nested\":[null,\"\",true]}}";
    private const string SameProperty = "same";
    private const string EmptyProperty = "";
    private const string NestedProperty = "nested";

    [Test]
    [Arguments(NegativeZero)]
    [Arguments(DecimalZeros)]
    [Arguments(Exponent)]
    [Arguments(BeyondDouble)]
    [Arguments(BeyondDecimal)]
    [Arguments(TinyExponent)]
    public async Task AcIs002NumericLexemeSurvivesNativeRoundTrip(string lexeme)
    {
        var value = JsonElement.Parse(lexeme);
        var payload = NativeSerialization.Serialize(new ValueOperand(value));
        var actual = NativeSerialization.Deserialize<ValueOperand>(payload);
        await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Number);
        await Assert.That(actual.Value.GetRawText()).IsEqualTo(lexeme);
        await Assert.That(NativeSerialization.Measure(new ValueOperand(value))).IsEqualTo((long)payload.Length);
    }

    [Test]
    public async Task AcIs002OrderedDuplicatePropertiesNullAndEmptyOwnTheirDom()
    {
        byte[] payload;
        using (var source = JsonDocument.Parse(OrderedDuplicates))
        {
            payload = NativeSerialization.Serialize(new ValueOperand(source.RootElement));
        }
        var actual = NativeSerialization.Deserialize<ValueOperand>(payload).Value;
        Array.Clear(payload);
        await Assert.That(actual.GetRawText()).IsEqualTo(OrderedDuplicates);
        await Assert.That(actual.EnumerateObject().Select(property => property.Name).ToArray())
            .IsEquivalentTo(new[] { SameProperty, SameProperty, EmptyProperty }, CollectionOrdering.Matching);
        var nested = actual.GetProperty(EmptyProperty).GetProperty(NestedProperty);
        await Assert.That(nested[0].ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(nested[1].GetString()).IsEqualTo(EmptyProperty);
        await Assert.That(nested[2].GetBoolean()).IsTrue();
    }

    [Test]
    public async Task AcIs002WrongNumericSurrogateLexemeIsRejected()
    {
        const string NumericLexeme = "1234";
        const string InvalidNumber = "null";
        var bytes = NativeSerialization.Serialize(new ValueOperand(JsonElement.Parse(NumericLexeme)));
        var index = bytes.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes(NumericLexeme));
        await Assert.That(index >= 0).IsTrue();
        System.Text.Encoding.UTF8.GetBytes(InvalidNumber).CopyTo(bytes, index);
        KeyLoadException? failure = null;
        try
        {
            _ = NativeSerialization.Deserialize<ValueOperand>(bytes);
        }
        catch (KeyLoadException exception)
        {
            failure = exception;
        }
        await Assert.That(failure).IsNotNull();
        await Assert.That(failure!.Code).IsEqualTo(ErrorCode.Corruption);
    }

    [Test]
    public async Task AcIs002DictionaryParametersUseTheSameOwnedElementAdapter()
    {
        const string Parameter = "amount";
        const string Number = "1.2300e+02";
        const string Sql = "SELECT * FROM records WHERE amount = @amount";
        var request = new QueryRequest(NativeContractCases.Partition, Sql,
            new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [Parameter] = JsonElement.Parse(Number) });
        var bytes = NativeSerialization.Serialize(request);
        var actual = NativeSerialization.Deserialize<QueryRequest>(bytes);
        Array.Clear(bytes);
        await Assert.That(actual.Parameters![Parameter].GetRawText()).IsEqualTo(Number);
        await Assert.That(actual.Parameters!.Comparer.Equals(Parameter, Parameter.ToUpperInvariant())).IsFalse();
    }
}
