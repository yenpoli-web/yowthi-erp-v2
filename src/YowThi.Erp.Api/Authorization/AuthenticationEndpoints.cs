using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;
using YowThi.Erp.Api.Errors;
using YowThi.Erp.Application.Security;

namespace YowThi.Erp.Api.Authorization;

public static class AuthenticationEndpoints
{
    private const string CsrfRequestCookieName = "XSRF-TOKEN";

    public static WebApplication MapYowThiAuthenticationEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet("/auth/session", SessionAsync)
            .AllowAnonymous()
            .ExcludeFromDescription()
            .WithName("Auth_Session");

        app.MapPost("/auth/logout", LogoutAsync)
            .RequireAuthorization()
            .ExcludeFromDescription()
            .WithName("Auth_Logout");

        var runtimeOptions = app.Services.GetRequiredService<SecurityRuntimeOptions>();
        if (runtimeOptions.DevelopmentTestAdminEnabled)
        {
            app.MapPost("/auth/development-login", DevelopmentLoginAsync)
                .AllowAnonymous()
                .ExcludeFromDescription()
                .WithName("Auth_DevelopmentLogin");

            app.MapPost("/auth/development-deletion-reauthenticate", DevelopmentDeletionReauthenticateAsync)
                .RequireAuthorization()
                .ExcludeFromDescription()
                .WithName("Auth_DevelopmentDeletionReauthenticate");
        }

        return app;
    }

    private static IResult SessionAsync(
        HttpContext context,
        IAntiforgery antiforgery,
        SecurityRuntimeOptions runtimeOptions)
    {
        IssueCsrfRequestToken(context, antiforgery);
        return TypedResults.Ok(CreateSessionResponse(context.User, runtimeOptions.DevelopmentTestAdminEnabled));
    }

    private static async Task<IResult> DevelopmentLoginAsync(
        HttpContext context,
        IDevelopmentTestAdminProvisioner provisioner,
        IAntiforgery antiforgery,
        SecurityRuntimeOptions runtimeOptions,
        CancellationToken cancellationToken)
    {
        if (!runtimeOptions.DevelopmentTestAdminEnabled)
        {
            return TypedResults.NotFound();
        }

        var actor = await provisioner.EnsureAsync(cancellationToken);
        var claims = new[]
        {
            new Claim(SecurityClaimTypes.AccountId, actor.AccountId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, actor.AccountId.ToString()),
            new Claim(ClaimTypes.Name, actor.DisplayName),
        };
        var principal = new ClaimsPrincipal(
            new ClaimsIdentity(
                claims,
                CookieAuthenticationDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role));

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
            });

        var authorizedClaims = new List<Claim>(claims);
        authorizedClaims.AddRange(actor.Capabilities.Select(capability =>
            new Claim(SecurityClaimTypes.Capability, capability)));
        var authorizedPrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                authorizedClaims,
                CookieAuthenticationDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role));
        context.User = authorizedPrincipal;

        IssueCsrfRequestToken(context, antiforgery);
        return TypedResults.Ok(CreateSessionResponse(authorizedPrincipal, true));
    }

    private static async Task<IResult> DevelopmentDeletionReauthenticateAsync(
        HttpContext context,
        IDevelopmentTestAdminProvisioner provisioner,
        IAntiforgery antiforgery,
        SecurityRuntimeOptions runtimeOptions,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        if (!runtimeOptions.DevelopmentTestAdminEnabled)
        {
            return TypedResults.NotFound();
        }

        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestAntiforgeryFailed,
                "Request antiforgery validation failed.");
        }

        if (!Guid.TryParse(context.User.FindFirstValue(SecurityClaimTypes.AccountId), out var currentAccountId))
        {
            return TypedResults.Unauthorized();
        }

        var actor = await provisioner.EnsureAsync(cancellationToken);
        if (actor.AccountId != currentAccountId)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status403Forbidden,
                ApiErrorCodes.DeletionReauthenticationRequired,
                "Automatic deletion re-authentication is available only for the Development Test Admin.");
        }

        var reauthenticatedAt = timeProvider.GetUtcNow();
        var cookieClaims = new[]
        {
            new Claim(SecurityClaimTypes.AccountId, actor.AccountId.ToString()),
            new Claim(ClaimTypes.NameIdentifier, actor.AccountId.ToString()),
            new Claim(ClaimTypes.Name, actor.DisplayName),
            DeletionReauthenticationClaims.CreateClaim(reauthenticatedAt),
        };
        var cookiePrincipal = new ClaimsPrincipal(
            new ClaimsIdentity(
                cookieClaims,
                CookieAuthenticationDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role));

        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            cookiePrincipal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
            });

        var authorizedClaims = new List<Claim>(cookieClaims);
        authorizedClaims.AddRange(actor.Capabilities.Select(capability =>
            new Claim(SecurityClaimTypes.Capability, capability)));
        context.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                authorizedClaims,
                CookieAuthenticationDefaults.AuthenticationScheme,
                ClaimTypes.Name,
                ClaimTypes.Role));

        IssueCsrfRequestToken(context, antiforgery);
        return TypedResults.NoContent();
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context,
        IAntiforgery antiforgery)
    {
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            return ApiProblemResults.Create(
                context,
                StatusCodes.Status400BadRequest,
                ApiErrorCodes.RequestAntiforgeryFailed,
                "Request antiforgery validation failed.");
        }

        await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        context.Response.Cookies.Delete(CsrfRequestCookieName, new CookieOptions
        {
            Secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    || context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            Path = "/",
        });
        return TypedResults.NoContent();
    }

    private static AuthenticationSessionResponse CreateSessionResponse(
        ClaimsPrincipal principal,
        bool developmentLoginAvailable)
    {
        if (principal.Identity?.IsAuthenticated != true
            || !Guid.TryParse(principal.FindFirstValue(SecurityClaimTypes.AccountId), out var accountId))
        {
            return new AuthenticationSessionResponse(
                false,
                null,
                null,
                [],
                developmentLoginAvailable);
        }

        var capabilities = principal.FindAll(SecurityClaimTypes.Capability)
            .Select(claim => claim.Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

        return new AuthenticationSessionResponse(
            true,
            accountId,
            principal.Identity.Name,
            capabilities,
            developmentLoginAvailable);
    }

    private static void IssueCsrfRequestToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        if (string.IsNullOrWhiteSpace(tokens.RequestToken))
        {
            throw new InvalidOperationException("Antiforgery request token could not be issued.");
        }

        context.Response.Cookies.Append(
            CsrfRequestCookieName,
            tokens.RequestToken,
            new CookieOptions
            {
                HttpOnly = false,
                Secure = !context.RequestServices.GetRequiredService<IHostEnvironment>().IsDevelopment()
                    || context.Request.IsHttps,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Path = "/",
            });
    }
}

public sealed record AuthenticationSessionResponse(
    bool Authenticated,
    Guid? AccountId,
    string? DisplayName,
    IReadOnlyList<string> Capabilities,
    bool DevelopmentLoginAvailable);
