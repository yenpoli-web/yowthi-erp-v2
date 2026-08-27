namespace YowThi.Erp.Application.Common.Identity;

public sealed record ActorAccountId
{
    private ActorAccountId(Guid value)
    {
        Value = value;
    }

    public Guid Value { get; }

    public static ActorAccountId From(Guid value)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("ActorAccountId cannot be empty.", nameof(value));
        }

        return new ActorAccountId(value);
    }

    public override string ToString() => Value.ToString();
}
