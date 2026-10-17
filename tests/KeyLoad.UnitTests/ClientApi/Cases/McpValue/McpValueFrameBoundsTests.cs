using System.Text;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-003/004/005: arbitrary JSON-value framing retains the shared strict parser and resource limits.</summary>
internal sealed class McpValueFrameBoundsTests
{
    private const int MaximumWireBytes = 1_000_000;
    private const int MaximumDepth = 64;
    private const int MaximumTokens = 131_072;
    private const int MaximumProperties = 32_768;
    private const int MaximumPropertyNameBytes = 256;

    /// <summary>JSON arrays, null, booleans, numbers, strings and objects are valid single value roots.</summary>
    /// <param name="frame">The JSON value to inspect.</param>
    /// <param name="expectedTokenCount">The number of reader tokens in the value.</param>
    [Test]
    [Arguments("[]", 2)]
    [Arguments("null", 1)]
    [Arguments("true", 1)]
    [Arguments("42", 1)]
    [Arguments("\"text\"", 1)]
    [Arguments("{}", 2)]
    public async Task InspectValueAcceptsSingleJsonValues(string frame, int expectedTokenCount)
    {
        var shape = McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes(frame), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(shape.TokenCount).IsEqualTo(expectedTokenCount);
        await Assert.That(shape.PropertyCount).IsEqualTo(0);
    }

    /// <summary>Only the value-aware inspection path accepts nonobject roots; object framing remains closed.</summary>
    [Test]
    public async Task ObjectInspectionStillRejectsNonObjectRoot()
    {
        var bytes = Encoding.UTF8.GetBytes("[1]");
        var valueShape = McpFrameBounds.InspectValue(bytes, MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(valueShape.TokenCount).IsEqualTo(3);

        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpFrameBounds.Inspect(bytes, MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>One arbitrary JSON value is required; malformed frames and trailing values use safe validation.</summary>
    /// <param name="frame">A malformed or multiple-root JSON payload.</param>
    [Test]
    [Arguments("")]
    [Arguments("[1,]")]
    [Arguments("null true")]
    [Arguments("{}{}")]
    public async Task InvalidOrTrailingValueIsRejected(string frame)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes(frame), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
    }

    /// <summary>Value framing rejects invalid encoded UTF-8 with the same safe validation detail as malformed JSON.</summary>
    [Test]
    public async Task InvalidUtf8ValueUsesFixedSafeDetail()
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue([0x22, 0xC3, 0x28, 0x22], MaximumWireBytes, UnitMcpOptions.Execution()));
        var malformed = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes("[1,]"), MaximumWireBytes, UnitMcpOptions.Execution()));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(malformed.Message);
    }

    /// <summary>Escaped lone high or low surrogates in names and values fail with one fixed safe Validation detail.</summary>
    /// <param name="frame">A JSON string containing an unpaired escaped surrogate.</param>
    [Test]
    [Arguments("""{"\uD800":0}""")]
    [Arguments("""{"\uDC00":0}""")]
    [Arguments("""{"value":"\uD800"}""")]
    [Arguments("""{"value":"\uDC00"}""")]
    public async Task EscapedUnpairedSurrogatesUseSafeValidation(string frame)
    {
        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes(frame), MaximumWireBytes, UnitMcpOptions.Execution()));
        var malformed = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes("{"), MaximumWireBytes, UnitMcpOptions.Execution()));

        await Assert.That(error.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(error.Message).IsEqualTo(malformed.Message);
    }

    /// <summary>Valid escaped surrogate pairs remain accepted in both property names and string values.</summary>
    [Test]
    public async Task EscapedSurrogatePairsAreValidInNamesAndValues()
    {
        var key = McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes("""{"\uD83D\uDE00":0}"""), MaximumWireBytes, UnitMcpOptions.Execution());
        var value = McpFrameBounds.InspectValue(Encoding.UTF8.GetBytes("""{"value":"\uD83D\uDE00"}"""), MaximumWireBytes, UnitMcpOptions.Execution());

        await Assert.That(key.PropertyCount).IsEqualTo(1);
        await Assert.That(value.PropertyCount).IsEqualTo(1);
    }

    /// <summary>Value framing accepts the exact maximum nesting and rejects the next container level.</summary>
    [Test]
    public async Task ValueDepthLimitIsSharedWithObjectFraming()
    {
        var exact = McpFrameBounds.InspectValue(NestedArrayFrame(MaximumDepth), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(exact.TokenCount).IsEqualTo((MaximumDepth * 2) + 1);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(NestedArrayFrame(MaximumDepth + 1), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>UTF-8 byte capacity includes its exact boundary and is exhausted by a shorter ceiling.</summary>
    [Test]
    public async Task ValueByteLimitIsSharedWithObjectFraming()
    {
        var bytes = Encoding.UTF8.GetBytes("\"é\"");
        var exact = McpFrameBounds.InspectValue(bytes, bytes.Length, UnitMcpOptions.Execution());
        await Assert.That(exact.TokenCount).IsEqualTo(1);

        var error = Assert.ThrowsExactly<KeyLoadException>(() => McpFrameBounds.InspectValue(bytes, bytes.Length - 1, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Token and property ceilings apply to arbitrary-root frames, including object-valued roots.</summary>
    [Test]
    public async Task ValueTokenAndPropertyLimitsAreSharedWithObjectFraming()
    {
        var exactTokens = McpFrameBounds.InspectValue(ValueArrayFrame(MaximumTokens - 2), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(exactTokens.TokenCount).IsEqualTo(MaximumTokens);
        var tokenError = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(ValueArrayFrame(MaximumTokens - 1), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(tokenError.Code).IsEqualTo(ErrorCode.ResourceExhausted);

        var exactProperties = McpFrameBounds.InspectValue(PropertyFrame(MaximumProperties), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(exactProperties.PropertyCount).IsEqualTo(MaximumProperties);
        var propertyError = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(PropertyFrame(MaximumProperties + 1), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(propertyError.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    /// <summary>Encoded property-name size remains bounded for value frames.</summary>
    [Test]
    public async Task ValuePropertyNameLimitIsSharedWithObjectFraming()
    {
        var exactName = new string('é', MaximumPropertyNameBytes / 2);
        var overName = new string('a', MaximumPropertyNameBytes - 1) + 'é';
        var exact = McpFrameBounds.InspectValue(PropertyFrame(exactName), MaximumWireBytes, UnitMcpOptions.Execution());
        await Assert.That(exact.PropertyCount).IsEqualTo(1);

        var error = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpFrameBounds.InspectValue(PropertyFrame(overName), MaximumWireBytes, UnitMcpOptions.Execution()));
        await Assert.That(error.Code).IsEqualTo(ErrorCode.ResourceExhausted);
    }

    private static byte[] NestedArrayFrame(int depth)
    {
        var json = new StringBuilder().Append('[', depth).Append('0').Append(']', depth);
        return Encoding.UTF8.GetBytes(json.ToString());
    }

    private static byte[] ValueArrayFrame(int scalarCount)
    {
        var json = new StringBuilder("[");
        for (var index = 0; index < scalarCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append('0');
        }

        return Encoding.UTF8.GetBytes(json.Append(']').ToString());
    }

    private static byte[] PropertyFrame(int propertyCount)
    {
        var json = new StringBuilder("{");
        for (var index = 0; index < propertyCount; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("\"p").Append(index).Append("\":0");
        }

        return Encoding.UTF8.GetBytes(json.Append('}').ToString());
    }

    private static byte[] PropertyFrame(string name)
        => Encoding.UTF8.GetBytes($"{{\"{name}\":0}}");
}
