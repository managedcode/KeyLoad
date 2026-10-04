namespace KeyLoad.Server;

/// <summary>Named JSON Schema keywords and the frozen MCP wrapper references.</summary>
internal static class McpSchemaKeywords
{
    internal const string Schema = "$schema";
    internal const string SchemaVersion = "https://json-schema.org/draft/2020-12/schema";
    internal const string Definitions = "$defs";
    internal const string Reference = "$ref";
    internal const string ReferenceRoot = "#";
    internal const string LocalReferencePrefix = "#/";
    internal const string RequestReference = "#/$defs/request";
    internal const string ResultReference = "#/$defs/result";
    internal const string ErrorReference = "#/$defs/error";
    internal const string Type = "type";
    internal const string Object = "object";
    internal const string String = "string";
    internal const string Integer = "integer";
    internal const string Null = "null";
    internal const string Properties = "properties";
    internal const string Required = "required";
    internal const string AdditionalProperties = "additionalProperties";
    internal const string PatternProperties = "patternProperties";
    internal const string DependentSchemas = "dependentSchemas";
    internal const string AnyOf = "anyOf";
    internal const string OneOf = "oneOf";
    internal const string AllOf = "allOf";
    internal const string Items = "items";
    internal const string PrefixItems = "prefixItems";
    internal const string Contains = "contains";
    internal const string Not = "not";
    internal const string If = "if";
    internal const string Then = "then";
    internal const string Else = "else";
    internal const string PropertyNames = "propertyNames";
    internal const string UnevaluatedProperties = "unevaluatedProperties";
    internal const string UnevaluatedItems = "unevaluatedItems";
    internal const string ContentSchema = "contentSchema";
    internal const string Enum = "enum";
    internal const string Const = "const";
    internal const string Default = "default";
    internal const string Title = "title";
    internal const string Description = "description";
    internal const string Examples = "examples";
    internal const string Minimum = "minimum";
    internal const string Maximum = "maximum";
    internal const string Format = "format";
    internal const string Uuid = "uuid";
    internal const string GuidFormat = "D";
    internal const string ContentEncoding = "contentEncoding";
    internal const string Base64 = "base64";
    internal const string ProblemType = "type";
    internal const string ProblemTitle = "title";
    internal const string ProblemStatus = "status";
    internal const string ProblemDetail = "detail";
    internal const string ProblemCode = "errorCode";
}
