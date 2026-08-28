using Microsoft.AspNetCore.Http;
using YowThi.Erp.Api.Idempotency;

namespace YowThi.Erp.Api.ContractTests;

public sealed class IdempotencyFilterTests
{
    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var httpContext = new DefaultHttpContext();
        var filter = new IdempotencyKeyEndpointFilter();

        var result = await filter.InvokeAsync(
            new TestInvocationContext(httpContext),
            static _ => ValueTask.FromResult<object?>(new object()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }

    [Fact]
    public async Task Invalid_idempotency_key_returns_400()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName] = "not-a-uuid";
        var filter = new IdempotencyKeyEndpointFilter();

        var result = await filter.InvokeAsync(
            new TestInvocationContext(httpContext),
            static _ => ValueTask.FromResult<object?>(new object()));

        var statusResult = Assert.IsAssignableFrom<IStatusCodeHttpResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, statusResult.StatusCode);
    }

    [Fact]
    public async Task Valid_idempotency_key_becomes_a_CommandId_transport_feature()
    {
        var expected = Guid.NewGuid();
        var sentinel = new object();
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[IdempotencyKeyEndpointFilter.HeaderName] = expected.ToString();
        var filter = new IdempotencyKeyEndpointFilter();

        var result = await filter.InvokeAsync(
            new TestInvocationContext(httpContext),
            _ => ValueTask.FromResult<object?>(sentinel));

        Assert.Same(sentinel, result);
        Assert.Equal(expected, httpContext.Features.Get<IdempotencyKeyFeature>()?.CommandId.Value);
    }

    private sealed class TestInvocationContext(HttpContext httpContext) : EndpointFilterInvocationContext
    {
        public override HttpContext HttpContext { get; } = httpContext;

        public override IList<object?> Arguments { get; } = [];

        public override T GetArgument<T>(int index)
        {
            return (T)Arguments[index]!;
        }
    }
}
