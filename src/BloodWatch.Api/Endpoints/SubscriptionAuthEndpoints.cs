using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using BloodWatch.Api.Contracts;
using BloodWatch.Api.Options;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BloodWatch.Api.Endpoints;

public static class SubscriptionAuthEndpoints
{
    private static readonly TimeSpan FailedLoginDelay = TimeSpan.FromMilliseconds(250);
    private static readonly PasswordHasher<string> PasswordHasher = new();

    public static IEndpointRouteBuilder MapSubscriptionAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
            .WithTags("Auth");

        group.MapPost("/token", CreateTokenAsync)
            .WithName("CreateToken")
            .WithSummary("Issue a short-lived JWT for subscription endpoints.")
            .RequireRateLimiting(ApiAuthConstants.AuthTokenRateLimitPolicyName)
            .Produces<CreateTokenResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .WithOpenApi();

        return app;
    }

    private static async Task<IResult> CreateTokenAsync(
        CreateTokenRequest request,
        IOptions<JwtAuthOptions> authOptions,
        CancellationToken cancellationToken)
    {
        var options = authOptions.Value;

        if (!TryResolveOperationalConfiguration(
                options,
                out var signingKeyBytes,
                out var issuer,
                out var audience,
                out var adminEmail,
                out var adminPasswordHash))
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Service unavailable",
                Detail = "Token issuance is unavailable because JWT auth configuration is missing.",
                Type = "https://httpstatuses.com/503",
            });
        }

        var providedEmail = NormalizeEmail(request.Email);
        var providedPassword = Normalize(request.Password);
        if (providedEmail is null || providedPassword is null)
        {
            return TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Bad request",
                Detail = "Email and password are required.",
                Type = "https://httpstatuses.com/400",
            });
        }

        var emailMatches = IsMatch(adminEmail, providedEmail);
        var passwordMatches = VerifyPassword(adminPasswordHash, providedPassword);

        if (!emailMatches || !passwordMatches)
        {
            await Task.Delay(FailedLoginDelay, cancellationToken);
            return TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Unauthorized",
                Detail = "Invalid credentials.",
                Type = "https://httpstatuses.com/401",
            });
        }

        var now = DateTime.UtcNow;
        var expiresAtUtc = now.AddMinutes(Math.Clamp(options.AccessTokenMinutes, 1, 120));

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, "admin"),
            new Claim(ApiAuthConstants.RoleClaimType, ApiAuthConstants.AdminRoleValue),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };

        var signingCredentials = new SigningCredentials(
            new SymmetricSecurityKey(signingKeyBytes),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            notBefore: now,
            expires: expiresAtUtc,
            signingCredentials: signingCredentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        return TypedResults.Ok(new CreateTokenResponse(accessToken, "Bearer", expiresAtUtc));
    }

    private static bool TryResolveOperationalConfiguration(
        JwtAuthOptions options,
        out byte[] signingKeyBytes,
        out string issuer,
        out string audience,
        out string adminEmail,
        out string adminPasswordHash)
    {
        signingKeyBytes = [];
        issuer = string.Empty;
        audience = string.Empty;
        adminEmail = string.Empty;
        adminPasswordHash = string.Empty;

        if (!options.Enabled)
        {
            return false;
        }

        issuer = Normalize(options.Issuer) ?? "bloodwatch-api";
        audience = Normalize(options.Audience) ?? "bloodwatch-clients";

        var signingKey = Normalize(options.SigningKey);
        var normalizedEmail = NormalizeEmail(options.AdminEmail);
        var normalizedPasswordHash = Normalize(options.AdminPasswordHash);
        if (signingKey is null || normalizedEmail is null || normalizedPasswordHash is null)
        {
            return false;
        }

        signingKeyBytes = Encoding.UTF8.GetBytes(signingKey);
        if (signingKeyBytes.Length < 32)
        {
            return false;
        }

        adminEmail = normalizedEmail;
        adminPasswordHash = normalizedPasswordHash;
        return true;
    }

    private static bool IsMatch(string? expected, string? provided)
    {
        if (expected is null || provided is null)
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        var providedBytes = Encoding.UTF8.GetBytes(provided);

        return expectedBytes.Length == providedBytes.Length
               && CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static string? NormalizeEmail(string? value)
    {
        var normalized = Normalize(value);
        return normalized?.ToLowerInvariant();
    }

    private static bool VerifyPassword(string expectedHash, string providedPassword)
    {
        try
        {
            var result = PasswordHasher.VerifyHashedPassword("bloodwatch-admin", expectedHash, providedPassword);
            return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
