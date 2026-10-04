using Microsoft.CodeAnalysis;

namespace KeyLoad.Analyzers.Features.CodeQuality;

internal static class NumericQualityRuleDescriptors
{
    private const string Category = "Design";
    private const string FileTitle = "File exceeds its code line limit";
    private const string FileMessage = "File contains {0} code lines; the maximum is 400";
    private const string FileDescription = "A C# source file may contain at most 400 code lines.";
    private const string TypeTitle = "Type exceeds its aggregate code line limit";
    private const string TypeMessage = "Type '{0}' contains {1} code lines; the maximum is 200";
    private const string TypeDescription = "A type across all partial declarations may contain at most 200 code lines.";
    private const string UnitTitle = "Executable unit exceeds its code line limit";
    private const string UnitMessage = "Executable unit contains {0} code lines; the maximum is 50";
    private const string UnitDescription = "An executable unit may contain at most 50 code lines.";
    private const string NestingTitle = "Control-flow nesting exceeds its limit";
    private const string NestingMessage = "Control-flow nesting is {0}; the maximum is 3";
    private const string NestingDescription = "An executable unit may nest at most three control-flow constructs.";

    internal static readonly DiagnosticDescriptor FileCodeLines = Create(
        "KLD0030", FileTitle, FileMessage, FileDescription);
    internal static readonly DiagnosticDescriptor AggregateTypeCodeLines = Create(
        "KLD0031", TypeTitle, TypeMessage, TypeDescription);
    internal static readonly DiagnosticDescriptor ExecutableUnitCodeLines = Create(
        "KLD0032", UnitTitle, UnitMessage, UnitDescription);
    internal static readonly DiagnosticDescriptor ControlFlowNesting = Create(
        "KLD0033", NestingTitle, NestingMessage, NestingDescription);

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message,
        string description) => new(
            id,
            title,
            message,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description: description);
}
