namespace KeyLoad.Server;

/// <summary>Named version-one catalog keys and fixed safe boundary failures.</summary>
internal static class McpCatalogProtocol
{
    internal const string Request = "request";
    internal const string CommandId = "commandId";
    internal const string Result = "result";
    internal const string Error = "error";
    internal const string RequestId = "requestId";
    internal const string InvalidArguments = "The tool arguments do not match the canonical operation contract.";
    internal const string StableCommandRequired = "A nonempty stable command identifier is required.";
    internal const string InvalidOperation = "The operation is not a public catalog capability.";
    internal const string ConverterConfiguration = "The canonical strict converter configuration does not match its schema projection.";
    internal const string InvalidSchemaReference = "The schema contains an unsupported reference.";
    internal const string UnsupportedSchema = "The canonical converter requires an explicit schema projection.";
    internal const string UnknownDescription = "The tool is absent from the frozen public catalog.";
    internal const int BodyArgumentCount = 1;
    internal const int HeaderArgumentCount = 2;
}
