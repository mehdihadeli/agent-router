using AgentRouter.Contracts.Configuration;

namespace AgentRouter.Storage.Configuration;

/// <summary>
/// Reads and writes the gateway configuration document. The document is stored without secrets;
/// secret values are split into the encrypted store on write and hydrated on read.
/// </summary>
public interface IConfigStore
{
    /// <summary>Loads the document, creating defaults on first run. Secret values are hydrated.</summary>
    Task<GatewayConfigDocument> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the document, extracting secret values and bumping the config version.</summary>
    Task SaveAsync(GatewayConfigDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Monotonic version of the persisted document. Increments on every save so the proxy can
    /// hot-reload cheaply by comparing a cached value.
    /// </summary>
    Task<long> GetConfigVersionAsync(CancellationToken cancellationToken = default);
}
