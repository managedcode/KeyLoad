using KeyLoad.Core.BlobStorage;
using ManagedCode.Storage.FileSystem;
using ManagedCode.Storage.FileSystem.Options;
using ManagedCode.Storage.Server.ChunkUpload;
using ManagedCode.Storage.Server.Controllers;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

[ConfigurationBinding]
internal static class FileStorageComposition
{
    internal const string ChunkDirectory = "file-chunks";

    internal static void Register(WebApplicationBuilder builder)
    {
        builder.Services.AddOptions<FileStorageOptions>()
            .Bind(builder.Configuration.GetSection(FileStorageOptions.SectionName))
            .Validate(value => value.IsValid(), FileStorageOptions.InvalidSettings).ValidateOnStart();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddControllers().AddApplicationPart(typeof(FileStorageController).Assembly)
            .ConfigureApplicationPartManager(manager => manager.FeatureProviders.Add(new FileStorageControllerDiscovery()));
        builder.Services.AddScoped<FileStorageResourceFilter>();
        builder.Services.AddSingleton<FileStorageTransferGate>();
        builder.Services.AddScoped(provider =>
        {
            var context = provider.GetRequiredService<IHttpContextAccessor>().HttpContext
                ?? throw Errors.Fail(ErrorCode.Unauthenticated, FileStorageProtocol.Invalid);
            var scope = context.Features.Get<FileStorageScope>()
                ?? throw Errors.Fail(ErrorCode.PermissionDenied, FileStorageProtocol.Invalid);
            return new FileSystemStorage(new FileSystemStorageOptions { BaseFolder = scope.Directory });
        });
        builder.Services.AddScoped<IFileSystemStorage>(provider => provider.GetRequiredService<FileSystemStorage>());
        builder.Services.AddScoped<FileStorageRequest>();
        builder.Services.AddScoped<FileStorageCursor>();
        builder.Services.AddSingleton(new StorageServerOptions());
        builder.Services.AddSingleton(provider => new ChunkUploadService(new ChunkUploadOptions
        {
            TempPath = Path.Combine(provider.GetRequiredService<IOptions<NodeOptions>>().Value.DataDirectory, ChunkDirectory),
            MaxActiveSessions = provider.GetRequiredService<IOptions<FileStorageOptions>>().Value.MaximumChunkSessions
        }));
    }
}
