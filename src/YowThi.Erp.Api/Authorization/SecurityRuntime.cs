using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Application.Common.Identity;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.Authorization;

public static class SecurityClaimTypes
{
    public const string AccountId = "yowthi:account-id";
    public const string Capability = "yowthi:capability";
}

public sealed record SecurityRuntimeOptions(bool DevelopmentTestAdminEnabled);

internal sealed class HttpActorContext : IActorContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpActorContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public ActorAccountId ActorAccountId
    {
        get
        {
            var value = _httpContextAccessor.HttpContext?.User.FindFirstValue(SecurityClaimTypes.AccountId);
            if (!Guid.TryParse(value, out var accountId) || accountId == Guid.Empty)
            {
                throw new InvalidOperationException("An authenticated active ERP account is required for actor resolution.");
            }

            return ActorAccountId.From(accountId);
        }
    }
}

internal sealed class SecurityActorResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityActorResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ISecurityActorResolver actorResolver)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var accountIdValue = context.User.FindFirstValue(SecurityClaimTypes.AccountId);
            var resolved = Guid.TryParse(accountIdValue, out var accountId)
                ? await actorResolver.ResolveAsync(accountId, context.RequestAborted)
                : null;

            if (resolved is null)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                context.User = new ClaimsPrincipal(new ClaimsIdentity());
            }
            else
            {
                var claims = new List<Claim>
                {
                    new(SecurityClaimTypes.AccountId, resolved.AccountId.ToString()),
                    new(ClaimTypes.NameIdentifier, resolved.AccountId.ToString()),
                    new(ClaimTypes.Name, resolved.DisplayName),
                };
                claims.AddRange(resolved.Capabilities.Select(capability =>
                    new Claim(SecurityClaimTypes.Capability, capability)));

                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        claims,
                        CookieAuthenticationDefaults.AuthenticationScheme,
                        ClaimTypes.Name,
                        ClaimTypes.Role));
            }
        }

        await _next(context);
    }
}

internal sealed class ApiAntiforgeryMiddleware
{
    private readonly RequestDelegate _next;

    public ApiAntiforgeryMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IAntiforgery antiforgery)
    {
        if (context.User.Identity?.IsAuthenticated == true
            && context.Request.Path.StartsWithSegments("/api/v1")
            && HttpMethods.IsPost(context.Request.Method))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                await ApiProblemResults.Create(
                        context,
                        StatusCodes.Status400BadRequest,
                        ApiErrorCodes.RequestAntiforgeryFailed,
                        "Request antiforgery validation failed.")
                    .ExecuteAsync(context);
                return;
            }
        }

        await _next(context);
    }
}
