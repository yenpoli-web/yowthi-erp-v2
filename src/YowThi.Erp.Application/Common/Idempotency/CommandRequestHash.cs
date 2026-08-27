namespace YowThi.Erp.Application.Common.Idempotency;

public sealed class CommandRequestHash : IEquatable<CommandRequestHash>
{
    public const int Sha256Length = 32;

    private readonly byte[] _bytes;

    private CommandRequestHash(byte[] bytes)
    {
        _bytes = bytes;
    }

    public ReadOnlyMemory<byte> Bytes => _bytes;

    public static CommandRequestHash FromSha256(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Sha256Length)
        {
            throw new ArgumentException($"Command request hash must be exactly {Sha256Length} bytes.", nameof(bytes));
        }

        return new CommandRequestHash(bytes.ToArray());
    }

    public bool Equals(CommandRequestHash? other) =>
        other is not null && _bytes.AsSpan().SequenceEqual(other._bytes);

    public override bool Equals(object? obj) => Equals(obj as CommandRequestHash);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(ToString());

    public override string ToString() => Convert.ToHexString(_bytes);
}
