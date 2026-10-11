namespace AgentRouter.Storage.Secrets;

/// <summary>
/// The AES-256-GCM key used to protect the secret store. Wrapped in a type (instead of a bare
/// <see cref="byte"/> array) so dependency injection can resolve it unambiguously.
/// </summary>
public sealed class MasterKey
{
    /// <summary>Required key size in bytes for AES-256.</summary>
    public const int KeySizeBytes = 32;

    public MasterKey(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != KeySizeBytes)
        {
            throw new ArgumentException(
                $"Master key must be exactly {KeySizeBytes} bytes, but was {value.Length}.",
                nameof(value)
            );
        }

        Value = value;
    }

    public byte[] Value { get; }

    /// <summary>Returns a copy so callers cannot mutate the key material held in DI.</summary>
    public byte[] ToArray() => (byte[])Value.Clone();
}
