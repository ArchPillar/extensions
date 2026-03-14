using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using WebShop.Data;
using WebShop.Models;
using WebShop.Parameters;

namespace WebShop.Endpoints;

/// <summary>Authentication endpoints: token issuance and self-registration.</summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        // OAuth 2.0 token endpoint (password grant + refresh token grant).
        app.MapPost("/connect/token", HandleTokenAsync)
           .ExcludeFromDescription();

        // Self-service registration — creates a Customer account and returns the user id.
        app.MapPost("/auth/register", RegisterAsync)
           .WithTags("Auth")
           .WithSummary("Register a new customer account.");

        return app;
    }

    private static async Task<IResult> HandleTokenAsync(
        HttpContext httpContext,
        UserManager<WebShopUser> userManager)
    {
        OpenIddictRequest request = httpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("OpenIddict server request is unavailable.");

        if (request.IsPasswordGrantType())
        {
            return await HandlePasswordGrantAsync(userManager, request);
        }

        if (request.IsRefreshTokenGrantType())
        {
            return await HandleRefreshTokenGrantAsync(httpContext, userManager);
        }

        return Results.BadRequest(new { error = "unsupported_grant_type" });
    }

    private static async Task<IResult> HandlePasswordGrantAsync(
        UserManager<WebShopUser> userManager,
        OpenIddictRequest request)
    {
        WebShopUser? user = await userManager.FindByEmailAsync(request.Username ?? string.Empty);

        if (user is null || !await userManager.CheckPasswordAsync(user, request.Password ?? string.Empty))
        {
            AuthenticationProperties properties = new(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error]            = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "Invalid email or password.",
            });

            return Results.Forbid(properties, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        return SignIn(user, request.GetScopes());
    }

    private static async Task<IResult> HandleRefreshTokenGrantAsync(
        HttpContext httpContext,
        UserManager<WebShopUser> userManager)
    {
        AuthenticateResult result = await httpContext.AuthenticateAsync(
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        var userId  = result.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
        WebShopUser? user = userId is not null ? await userManager.FindByIdAsync(userId) : null;

        if (user is null)
        {
            AuthenticationProperties properties = new(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error]            = OpenIddictConstants.Errors.InvalidGrant,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = "The refresh token is no longer valid.",
            });

            return Results.Forbid(properties, [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
        }

        return SignIn(user, result.Principal?.GetScopes() ?? []);
    }

    private static IResult SignIn(WebShopUser user, IEnumerable<string> scopes)
    {
        ClaimsIdentity identity = new(
            authenticationType: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
            nameType: OpenIddictConstants.Claims.Name,
            roleType: OpenIddictConstants.Claims.Role);

        identity.SetClaim(OpenIddictConstants.Claims.Subject, user.Id.ToString());
        identity.SetClaim(OpenIddictConstants.Claims.Email,   user.Email);
        identity.SetClaim(OpenIddictConstants.Claims.Name,    user.Email);
        identity.SetClaim("role",                             user.Role);

        ClaimsPrincipal principal = new(identity);
        principal.SetScopes(scopes);
        principal.SetDestinations(GetDestinations);

        return Results.SignIn(
            principal,
            authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static IEnumerable<string> GetDestinations(Claim claim)
    {
        yield return OpenIddictConstants.Destinations.AccessToken;

        if (claim.Type is OpenIddictConstants.Claims.Email
                       or OpenIddictConstants.Claims.Name
                       or "role")
        {
            yield return OpenIddictConstants.Destinations.IdentityToken;
        }
    }

    private static async Task<IResult> RegisterAsync(
        RegisterParameters parameters,
        UserManager<WebShopUser> userManager,
        WebShopDbContext db)
    {
        if (await userManager.FindByEmailAsync(parameters.Email) is not null)
        {
            return Results.Conflict(new { error = "Email already in use." });
        }

        WebShopUser user = new()
        {
            Id        = Guid.NewGuid(),
            UserName  = parameters.Email,
            Email     = parameters.Email,
            Role      = "Customer",
            CreatedAt = DateTime.UtcNow,
        };

        IdentityResult result = await userManager.CreateAsync(user, parameters.Password);

        if (!result.Succeeded)
        {
            return Results.ValidationProblem(
                result.Errors.ToDictionary(e => e.Code, e => new[] { e.Description }));
        }

        Customer customer = new()
        {
            Id              = Guid.NewGuid(),
            UserId          = user.Id,
            FirstName       = parameters.FirstName,
            LastName        = parameters.LastName,
            PhoneNumber     = parameters.PhoneNumber,
            ShippingAddress = null,
        };

        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        return Results.Created("/users/me", new { user.Id, user.Email });
    }
}
