namespace KeyLoad.Server;

internal static class AuthorizationApi
{
    private const string ResourcesPath = "/admin/resources";
    private const string PrincipalsPath = "/admin/principals";
    private const string ApiKeysPath = "/admin/api-keys";

    internal static void Map(WebApplication app)
    {
        app.MapPost(ResourcesPath, (ConfigureResourceRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigureResource, ApiGrainDispatch.CommandId(context), request));
        app.MapPost(PrincipalsPath, (ConfigurePrincipalRequest request, HttpContext context) =>
            ApiGrainDispatch.SubmitAsync(context, OperationKind.ConfigurePrincipal, ApiGrainDispatch.CommandId(context), request));
        global::ManagedCode.Communication.AspNetCore.MinimalApi.CommunicationEndpointExtensions.WithCommunicationResults(
            app.MapPost(ApiKeysPath, (ConfigureApiKeyRequest request, HttpContext context) =>
                ApiGrainDispatch.SubmitStatusAsync(context, OperationKind.ConfigureApiKey, ApiGrainDispatch.CommandId(context), request)));
    }
}
