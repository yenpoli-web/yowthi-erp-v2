namespace YowThi.Erp.Application.Common.Errors;

public sealed record ApplicationError
{
    private ApplicationError(ApplicationErrorKind kind, string code)
    {
        Kind = kind;
        Code = code;
    }

    public ApplicationErrorKind Kind { get; }

    public string Code { get; }

    public static ApplicationError Create(ApplicationErrorKind kind, string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Application error code is required.", nameof(code));
        }

        if (!string.Equals(code, code.Trim(), StringComparison.Ordinal))
        {
            throw new ArgumentException("Application error code cannot contain leading or trailing whitespace.", nameof(code));
        }

        return new ApplicationError(kind, code);
    }
}
