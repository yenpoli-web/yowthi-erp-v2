namespace YowThi.Erp.Application.Common.Identity;

public sealed record CommandId
{
    private CommandId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static CommandId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("CommandId cannot be empty.", nameof(value));
        }

        return new CommandId(value);
    }

    public override string ToString() => Value.ToString();
}
