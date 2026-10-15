using System.Security.Cryptography;
using AgentRouter.Core.Contracts.Defaults;

namespace AgentRouter.Core.Storage.Secrets;

/// <summary>
/// Resolves the master key. Precedence mirrors the reference implementation: an explicit
/// environment value, then the persisted key file, then a freshly generated key written with
/// owner-only permissions.
/// </summary>
public static class MasterKeyProvider
{
    /// <summary>
    /// Resolves the master key for <paramref name="paths"/>.
    /// </summary>
    /// <param name="paths">Gateway data directory.</param>
    /// <param name="environmentOverride">
    /// Hex-encoded key, normally read from <c>AR_SECRET_KEY</c>.
    /// </param>
    /// <param name="logger">Optional logger for key-creation diagnostics.</param>
    /// <exception cref="InvalidOperationException">A provided key is present but malformed.</exception>
    public static MasterKey Resolve(
        GatewayPaths paths,
        string? environmentOverride = null,
        ILogger? logger = null
    )
    {
        ArgumentNullException.ThrowIfNull(paths);

        environmentOverride ??= Environment.GetEnvironmentVariable(
            GatewayDefaults.SecretKeyEnvironmentVariable
        );

        if (!string.IsNullOrWhiteSpace(environmentOverride))
        {
            return new MasterKey(
                ParseHex(
                    environmentOverride,
                    $"{GatewayDefaults.SecretKeyEnvironmentVariable} is not a valid {MasterKey.KeySizeBytes}-byte hex key."
                )
            );
        }

        if (File.Exists(paths.MasterKeyPath))
        {
            var contents = File.ReadAllText(paths.MasterKeyPath).Trim();

            return new MasterKey(
                ParseHex(
                    contents,
                    $"The master key file at '{paths.MasterKeyPath}' is not a valid {MasterKey.KeySizeBytes}-byte hex key."
                )
            );
        }

        var generated = RandomNumberGenerator.GetBytes(MasterKey.KeySizeBytes);
        paths.EnsureCreated();
        if (WriteKeyFile(paths.MasterKeyPath, generated))
        {
            logger?.LogInformation(
                "Generated a new gateway master key at {MasterKeyPath}.",
                paths.MasterKeyPath
            );
            return new MasterKey(generated);
        }
        return new MasterKey(
            ParseHex(
                File.ReadAllText(paths.MasterKeyPath),
                $"The master key file at '{paths.MasterKeyPath}' is not a valid {MasterKey.KeySizeBytes}-byte hex key."
            )
        );
    }

    private static byte[] ParseHex(string value, string errorMessage)
    {
        var trimmed = value.Trim();

        if (trimmed.Length != MasterKey.KeySizeBytes * 2)
        {
            throw new InvalidOperationException(errorMessage);
        }

        try
        {
            return Convert.FromHexString(trimmed);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(errorMessage, exception);
        }
    }

    private static bool WriteKeyFile(string path, byte[] key)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary, Convert.ToHexString(key).ToLowerInvariant());
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            try
            {
                File.Move(temporary, path, overwrite: false);
                return true;
            }
            catch (IOException) when (File.Exists(path))
            {
                return false;
            }
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
