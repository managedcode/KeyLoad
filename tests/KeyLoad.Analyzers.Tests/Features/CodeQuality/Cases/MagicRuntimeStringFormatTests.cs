namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-030 and AC-CQ-031: native formatting policy distinguishes literal formats from data.</summary>
internal sealed class MagicRuntimeStringFormatTests
{
    [Test]
    [Arguments("number.ToString(format: [|\"X8\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("floating.ToString([|\"G17\"|], CultureInfo.InvariantCulture)")]
    [Arguments("amount.ToString([|\"F2\"|], CultureInfo.InvariantCulture)")]
    [Arguments("identifier.ToString(format: [|\"D\"|])")]
    [Arguments("timestamp.ToString(format: [|\"O\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("offset.ToString([|\"O\"|], CultureInfo.InvariantCulture)")]
    [Arguments("duration.ToString(format: [|\"c\"|], formatProvider: CultureInfo.InvariantCulture)")]
    [Arguments("date.ToString([|\"O\"|], CultureInfo.InvariantCulture)")]
    [Arguments("time.ToString([|\"O\"|], CultureInfo.InvariantCulture)")]
    public async Task NativeToStringFormatArgumentsRequireNamedTokensAsync(string expression)
    {
        var source = $$"""
            using System;
            using System.Globalization;
            internal static class Subject
            {
                internal static string Execute(int number, double floating, decimal amount,
                    Guid identifier, DateTime timestamp, DateTimeOffset offset, TimeSpan duration,
                    DateOnly date, TimeOnly time) =>
                    {{expression}};
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    [Arguments("number.TryFormat(buffer, out _, format: [|\"X8\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("floating.TryFormat(buffer, out _, format: [|\"G17\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("amount.TryFormat(buffer, out _, format: [|\"F2\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("identifier.TryFormat(buffer, out _, format: [|\"D\"|])")]
    [Arguments("timestamp.TryFormat(buffer, out _, format: [|\"O\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("offset.TryFormat(buffer, out _, format: [|\"O\"|], formatProvider: CultureInfo.InvariantCulture)")]
    [Arguments("duration.TryFormat(buffer, out _, format: [|\"c\"|], formatProvider: CultureInfo.InvariantCulture)")]
    [Arguments("date.TryFormat(buffer, out _, format: [|\"O\"|], provider: CultureInfo.InvariantCulture)")]
    [Arguments("time.TryFormat(buffer, out _, format: [|\"O\"|], provider: CultureInfo.InvariantCulture)")]
    public async Task NativeTryFormatConversionAndNamedParametersRetainFullStringSpanAsync(string expression)
    {
        var source = $$"""
            using System;
            using System.Globalization;
            internal static class Subject
            {
                internal static bool Execute(Span<char> buffer, int number, double floating, decimal amount,
                    Guid identifier, DateTime timestamp, DateTimeOffset offset, TimeSpan duration,
                    DateOnly date, TimeOnly time) =>
                    {{expression}};
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task ConstFormatsPreserveNativeStringAndSpanFormattingAsync()
    {
        const string source = """
            using System;
            using System.Globalization;
            internal static class Subject
            {
                private const string FixedHexadecimalFormat = "X8";
                private const string RoundTripDateFormat = "O";
                private const string DurationFormat = "c";
                internal static bool Execute(Span<char> buffer, int number, DateTime timestamp, TimeSpan duration)
                {
                    number.ToString(FixedHexadecimalFormat, CultureInfo.InvariantCulture);
                    timestamp.ToString(RoundTripDateFormat, CultureInfo.InvariantCulture);
                    duration.ToString(DurationFormat, CultureInfo.InvariantCulture);
                    return number.TryFormat(buffer, out _, FixedHexadecimalFormat, CultureInfo.InvariantCulture);
                }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    public async Task CallerFormatsRemainDynamicAndInterpolationTextRequiresIdentityAsync()
    {
        const string source = """
            using System;
            internal static class Subject
            {
                internal static string Execute(int number, int width, string callerFormat, bool decision)
                {
                    number.ToString();
                    decision.ToString();
                    number.ToString(callerFormat);
                    return number.ToString($"[|X|]{width}");
                }
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }

    [Test]
    [Arguments("byte")]
    [Arguments("sbyte")]
    [Arguments("short")]
    [Arguments("ushort")]
    [Arguments("uint")]
    [Arguments("long")]
    [Arguments("ulong")]
    [Arguments("float")]
    public async Task NativeNumericPrimitiveFormattingRequiresNamedTokensAsync(string numericType)
    {
        var source = $$"""
            using System.Globalization;
            internal static class Subject
            {
                internal static string Execute({{numericType}} number) =>
                    number.ToString([|"G"|], CultureInfo.InvariantCulture);
            }
            """;

        await MagicRuntimeFixture.AssertStringAsync(source);
    }
}
