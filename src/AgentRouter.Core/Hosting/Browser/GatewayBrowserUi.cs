using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace AgentRouter.Core.Hosting.Browser;

public static class GatewayBrowserUi
{
    public static void MapGatewayBrowserUi(this WebApplication app)
    {
        var directory = Path.GetFullPath(
            app.Configuration["BrowserUi:Directory"] ?? Path.Combine(AppContext.BaseDirectory, "ui")
        );
        app.MapGet("/", () => Results.Redirect("/ui/"));
        if (!Directory.Exists(directory))
        {
            app.MapGet(
                "/ui/{**path}",
                () =>
                    Results.Problem(
                        statusCode: 503,
                        title: "Management UI assets are not installed."
                    )
            );
            return;
        }
        var files = new PhysicalFileProvider(directory);
        app.Lifetime.ApplicationStopped.Register(files.Dispose);
        var contentTypes = new FileExtensionContentTypeProvider();
        app.MapGet(
            "/ui/{**path}",
            (string? path) =>
            {
                if (path?.Contains("..", StringComparison.Ordinal) == true)
                    return Results.NotFound() as IResult;
                if (!string.IsNullOrEmpty(path))
                {
                    var file = files.GetFileInfo(path);
                    if (
                        file.Exists
                        && !file.IsDirectory
                        && contentTypes.TryGetContentType(file.Name, out var contentType)
                    )
                        return Results.File(file.PhysicalPath!, contentType);
                    if (!string.IsNullOrEmpty(Path.GetExtension(path)))
                        return Results.NotFound();
                }
                return Results.File(Path.Combine(directory, "index.html"), "text/html");
            }
        );
    }
}
