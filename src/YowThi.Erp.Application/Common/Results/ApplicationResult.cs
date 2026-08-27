using YowThi.Erp.Application.Common.Errors;

namespace YowThi.Erp.Application.Common.Results;

public sealed class ApplicationResult
{
    private readonly ApplicationError? _error;

    private ApplicationResult(ApplicationError? error)
    {
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    public ApplicationError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A successful result does not contain an error.");

    public static ApplicationResult Success() => new(null);

    public static ApplicationResult Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApplicationResult(error);
    }
}
