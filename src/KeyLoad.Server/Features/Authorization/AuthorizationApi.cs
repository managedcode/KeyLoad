namespace KeyLoad.Server;

internal static class AuthorizationApi
{
    private const string ResourcesPath = "/v1/admin/resources";
    private const string PrincipalsPath = "/v1/admin/principals";
    private const string ApiKeysPath = "/v1/admin/api-keys";

    internal static void Map(WebApplication app)
    {
        app.MapPost(ResourcesPath, (ConfigureResourceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigureResource, ApiGrainDispatch.CommandId(context), request));
        app.MapPost(PrincipalsPath, (ConfigurePrincipalRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigurePrincipal, ApiGrainDispatch.CommandId(context), request));
        app.MapPost(ApiKeysPath, (ConfigureApiKeyRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigureApiKey, ApiGrainDispatch.CommandId(context), request));
    }
}
