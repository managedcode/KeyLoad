namespace KeyLoad.Analyzers.CodeQuality;

internal static class UntypedContractPolicy
{
    internal const string ContractId = "KLD0041";
    internal const string ConversionId = "KLD0042";
    internal const string ContractTitle = "Keep data contracts typed";
    internal const string ContractMessage = "Untyped {0} '{1}' requires a concrete type or an exact-site reason";
    internal const string ContractDescription = "Object, dynamic and non-generic collections require explicit review at their declaration and every use. Nested result, task and collection shapes are checked semantically. An exact trailing reason applies only to that syntax node.";
    internal const string ConversionTitle = "Review object boxing and runtime casts";
    internal const string ConversionMessage = "'{0}' from '{1}' to '{2}' requires typed data flow or an exact-site reason";
    internal const string ConversionDescription = "Value boxing and conversions from object require explicit review at the conversion itself. Reference upcasts do not imply allocation and are not boxing findings.";
    internal const string Declaration = "declaration";
    internal const string Invocation = "invocation";
    internal const string Member = "member access";
    internal const string Creation = "creation";
    internal const string Boxing = "boxing";
    internal const string PotentialBoxing = "potential generic boxing";
    internal const string ObjectCast = "runtime object conversion";
    internal const string CommentStart = "/* ";
    internal const string CommentEnd = " */";
    internal const string ReasonSeparator = ": ";
    internal const int MinimumReasonCharacters = 16;
    internal static readonly string[] UntypedCollections =
    [
        "System.Collections.IEnumerable",
        "System.Collections.IEnumerator",
        "System.Collections.ICollection",
        "System.Collections.IList",
        "System.Collections.IDictionary",
        "System.Collections.ArrayList",
        "System.Collections.Hashtable",
        "System.Collections.Queue",
        "System.Collections.Stack"
    ];
}
