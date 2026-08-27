namespace YowThi.Erp.Application.Common.Idempotency;

public sealed record CommandType
{
    private CommandType(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static CommandType From(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (!string.Equals(value, value.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Command type cannot contain leading or trailing whitespace.", nameof(value));
        }

        return new CommandType(value);
    }

    public override string ToString() => Value;
}
