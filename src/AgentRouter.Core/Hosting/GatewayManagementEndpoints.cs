using AgentRouter.Contracts.Configuration;

namespace AgentRouter;

/// <summary>Explicit provider settings; omitted key preserves an existing credential during updates.</summary>
public sealed record WriteProviderRequest(
    string BaseUrl,
    string[] Models,
    string? ApiKey = null,
    ProviderCompatibility Compatibility = ProviderCompatibility.OpenAi,
    bool Enabled = true
);

/// <summary>Isolated Claude Code profile settings.</summary>
public sealed record WriteProfileRequest(
    string? Model = null,
    string Executable = "claude",
    bool Enabled = true
);

/// <summary>Model selector assigned to a routing tier.</summary>
public sealed record WriteRouteRequest(string Model);

/// <summary>Public provider settings without stored credential values.</summary>
public sealed record ManagementProviderResponse(
    string Id,
    bool Enabled,
    ProviderCompatibility Compatibility,
    string? BaseUrl,
    string[] Models,
    bool HasApiKey
);

/// <summary>Isolated launch profile settings returned to management clients.</summary>
public sealed record ManagementProfileResponse(
    string Name,
    bool Enabled,
    string? Model,
    string Executable
);

/// <summary>Configured model-tier mapping.</summary>
public sealed record ManagementRouteResponse(
    string Tier,
    bool Enabled,
    string? ProviderId,
    string? Model
);

/// <summary>Safe configuration view; credentials and unrelated persisted settings are never included.</summary>
public sealed record ManagementConfigurationResponse(
    int ProxyPort,
    string? ActiveProvider,
    bool HasClientToken,
    ManagementProviderResponse[] Providers,
    ManagementProfileResponse[] Profiles,
    ManagementRouteResponse[] Routes,
    bool RestartRequired
);

public static class GatewayManagementEndpoints
{
    public static ManagementConfigurationResponse ToResponse(GatewayConfigDocument document) =>
        new(
            document.Server.ProxyPort,
            document.ActiveProvider,
            !string.IsNullOrWhiteSpace(document.Server.AuthToken),
            document
                .Providers.Select(entry => new ManagementProviderResponse(
                    entry.Key,
                    entry.Value.Enabled,
                    entry.Value.Compatibility,
                    entry.Value.BaseUrl,
                    entry.Value.Models.ToArray(),
                    !string.IsNullOrWhiteSpace(entry.Value.ApiKey)
                ))
                .ToArray(),
            (document.Profiles ?? [])
                .Select(profile => new ManagementProfileResponse(
                    profile.Name,
                    profile.Enabled,
                    profile.Model,
                    profile.Executable
                ))
                .ToArray(),
            document
                .Routing.AsMap()
                .Select(entry => new ManagementRouteResponse(
                    entry.Key.ToString(),
                    entry.Value.Enabled,
                    entry.Value.ProviderId,
                    entry.Value.Model
                ))
                .ToArray(),
            true
        );

    public static void MapManagementConfiguration(this WebApplication app)
    {
        var group = app.MapGroup("/management/v1");
        group.AddEndpointFilter(
            async (context, next) =>
            {
                try
                {
                    return await next(context);
                }
                catch (KeyNotFoundException)
                {
                    return Results.Problem(statusCode: 404, title: "Resource was not found.");
                }
                catch (ArgumentException)
                {
                    return Results.Problem(statusCode: 400, title: "Invalid configuration input.");
                }
                catch (NotSupportedException)
                {
                    return Results.Problem(statusCode: 400, title: "Unsupported configuration.");
                }
                catch (InvalidOperationException)
                {
                    return Results.Problem(
                        statusCode: 409,
                        title: "Configuration conflict. Check existing profiles and routes."
                    );
                }
            }
        );
        group
            .MapGet(
                "/providers",
                async (
                    GatewayConfigurationService configuration,
                    CancellationToken cancellationToken
                ) => ToResponse(await configuration.LoadAsync(cancellationToken)).Providers
            )
            .WithSummary("List provider settings without credentials.");
        group
            .MapPost(
                "/providers/{id}",
                async (
                    string id,
                    WriteProviderRequest request,
                    GatewayConfigurationService configuration,
                    CancellationToken cancellationToken
                ) =>
                {
                    ArgumentNullException.ThrowIfNull(request.Models);
                    if (!request.Enabled)
                        throw new ArgumentException("Create enabled provider first.");
                    await configuration.AddProviderAsync(
                        id,
                        request.BaseUrl,
                        request.ApiKey!,
                        request.Models,
                        cancellationToken,
                        request.Compatibility
                    );
                    return Results.Created(
                        $"/management/v1/providers/{Uri.EscapeDataString(id)}",
                        new { id, restartRequired = true }
                    );
                }
            )
            .WithSummary("Create an API-key provider; restart inference to apply.");
        group
            .MapPut(
                "/providers/{id}",
                async (
                    string id,
                    WriteProviderRequest request,
                    GatewayConfigurationService configuration,
                    CancellationToken cancellationToken
                ) =>
                {
                    await configuration.UpdateProviderAsync(
                        id,
                        request.BaseUrl,
                        request.ApiKey,
                        request.Models,
                        request.Enabled,
                        request.Compatibility,
                        cancellationToken
                    );
                    return Results.NoContent();
                }
            )
            .WithSummary("Update provider settings; restart inference to apply.");
        group
            .MapDelete(
                "/providers/{id}",
                async (
                    string id,
                    GatewayConfigurationService configuration,
                    CancellationToken cancellationToken
                ) =>
                {
                    await configuration.RemoveProviderAsync(id, cancellationToken);
                    return Results.NoContent();
                }
            )
            .WithSummary("Remove an unreferenced provider and its encrypted credentials.");
        group.MapGet(
            "/profiles",
            async (
                GatewayConfigurationService configuration,
                CancellationToken cancellationToken
            ) => ToResponse(await configuration.LoadAsync(cancellationToken)).Profiles
        );
        group.MapPost(
            "/profiles/{name}",
            async (
                string name,
                WriteProfileRequest request,
                GatewayConfigurationService configuration,
                CancellationToken cancellationToken
            ) =>
            {
                if (!request.Enabled)
                    throw new ArgumentException("Create enabled profile first.");
                await configuration.AddProfileAsync(
                    name,
                    request.Model,
                    request.Executable,
                    cancellationToken
                );
                return Results.Created(
                    $"/management/v1/profiles/{Uri.EscapeDataString(name)}",
                    new { name }
                );
            }
        );
        group.MapPut(
            "/profiles/{name}",
            async (
                string name,
                WriteProfileRequest request,
                GatewayConfigurationService configuration,
                CancellationToken cancellationToken
            ) =>
            {
                await configuration.UpdateProfileAsync(
                    name,
                    request.Model,
                    request.Executable,
                    request.Enabled,
                    cancellationToken
                );
                return Results.NoContent();
            }
        );
        group.MapDelete(
            "/profiles/{name}",
            async (
                string name,
                GatewayConfigurationService configuration,
                CancellationToken cancellationToken
            ) =>
            {
                await configuration.RemoveProfileAsync(name, cancellationToken);
                return Results.NoContent();
            }
        );
        group
            .MapPut(
                "/routes/{tier}",
                async (
                    string tier,
                    WriteRouteRequest request,
                    GatewayConfigurationService configuration,
                    CancellationToken cancellationToken
                ) =>
                {
                    if (
                        !Enum.TryParse<RoutingTier>(tier, ignoreCase: true, out var parsedTier)
                        || !Enum.IsDefined(parsedTier)
                    )
                        throw new ArgumentException("Unknown routing tier.");
                    await configuration.SetRouteAsync(request.Model, parsedTier, cancellationToken);
                    return Results.NoContent();
                }
            )
            .WithSummary("Assign default or Claude model-tier route; restart inference to apply.");
    }
}
