using YowThi.Erp.Application.Common.Errors;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Common.Results;

namespace YowThi.Erp.ArchitectureTests;

public sealed class ApplicationFoundationTests
{
    [Fact]
    public void CommandId_rejects_empty_guid()
    {
        Assert.Throws<ArgumentException>(() => CommandId.From(Guid.Empty));
    }

    [Fact]
    public void ActorAccountId_rejects_empty_guid()
    {
        Assert.Throws<ArgumentException>(() => ActorAccountId.From(Guid.Empty));
    }

    [Fact]
    public void ApplicationError_requires_stable_nonblank_code()
    {
        Assert.Throws<ArgumentException>(() =>
            ApplicationError.Create(ApplicationErrorKind.Conflict, "  "));

        Assert.Throws<ArgumentException>(() =>
            ApplicationError.Create(ApplicationErrorKind.Conflict, " sales.invalid-state "));
    }

    [Fact]
    public void Generic_result_exposes_value_only_on_success()
    {
        var success = ApplicationResult<int>.Success(42);
        var error = ApplicationError.Create(ApplicationErrorKind.Conflict, "test.conflict");
        var failure = ApplicationResult<int>.Failure(error);

        Assert.True(success.IsSuccess);
        Assert.Equal(42, success.Value);
        Assert.Throws<InvalidOperationException>(() => success.Error);

        Assert.True(failure.IsFailure);
        Assert.Same(error, failure.Error);
        Assert.Throws<InvalidOperationException>(() => failure.Value);
    }
}
