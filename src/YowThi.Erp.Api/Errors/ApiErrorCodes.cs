namespace YowThi.Erp.Api.Errors;

public static class ApiErrorCodes
{
    public const string RequestValidationFailed = "request.validation-failed";
    public const string RequestRateLimitExceeded = "request.rate-limit-exceeded";
    public const string RequestAntiforgeryFailed = "request.antiforgery-failed";
    public const string IdempotencyKeyMissing = "idempotency.key-missing";
    public const string IdempotencyKeyInvalid = "idempotency.key-invalid";
}
