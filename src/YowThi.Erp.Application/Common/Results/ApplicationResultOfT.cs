using YowThi.Erp.Application.Common.Errors;

namespace YowThi.Erp.Application.Common.Results;

public sealed class ApplicationResult<T>
{
    private readonly ApplicationError? _error;
    private readonly T? _value;

    private ApplicationResult(T? value, ApplicationError? error)
    {
        _value = value;
        _error = error;
    }

    public bool IsSuccess => _error is null;

    public bool IsFailure => !IsSuccess;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("A failed result does not contain a value.");

    public ApplicationError Error => IsFailure
        ? _error!
        : throw new InvalidOperationException("A successful result does not contain an error.");

    public static ApplicationResult<T> Success(T value) => new(value, null);

    public static ApplicationResult<T> Failure(ApplicationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new ApplicationResult<T>(default, error);
    }
}
