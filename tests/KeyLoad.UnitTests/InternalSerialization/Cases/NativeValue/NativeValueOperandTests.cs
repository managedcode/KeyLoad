using System.Text.Json;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.InternalSerialization;

internal sealed class NativeValueOperandTests
{
    [Test]
    [Arguments("null")]
    [Arguments("true")]
    [Arguments("1.10")]
    [Arguments("\"界λ\"")]
    [Arguments("[1,false,null]")]
    [Arguments("{\"a\":1,\"a\":2}")]
    public async Task StaticFactoryAndNativeOperandPreserveThePublicJsonValue(string json)
    {
        using var document = JsonDocument.Parse(json);
        var operand = ValueOperand.Create(document.RootElement);
        var restored = NativeSerialization.Deserialize<ValueOperand>(NativeSerialization.Serialize(operand));
        await Assert.That(restored.Value.GetRawText()).IsEqualTo(operand.Value.GetRawText());
        var publicBytes = JsonDefaults.Serialize(operand);
        var decoded = JsonDefaults.Deserialize<ValueOperand>(publicBytes);
        await Assert.That(decoded.Value.GetRawText()).IsEqualTo(restored.Value.GetRawText());
    }

    [Test]
    public async Task StaticFactoryAcceptsObjectLiteralsWithoutAnInstanceConstructor()
    {
        await Assert.That(ValueOperand.Create(null).Value.ValueKind).IsEqualTo(JsonValueKind.Null);
        await Assert.That(ValueOperand.Create(true).Value.GetBoolean()).IsTrue();
        await Assert.That(ValueOperand.Create(42).Value.GetInt32()).IsEqualTo(42);
        await Assert.That(ValueOperand.Create("界λ").Value.GetString()).IsEqualTo("界λ");
    }
}
