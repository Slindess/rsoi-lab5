using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Booking.Security;

public static class BookingAuthentication
{
    // Read JWKS once at startup. Token validation never calls the identity provider.
    // Restart services after rotating realm signing keys.
    public static async Task AddBookingAuthenticationAsync(this WebApplicationBuilder builder)
    {
        var issuer = builder.Configuration["Authentication:Issuer"]
            ?? "http://localhost:8081/auth/realms/rsoi";
        var jwksUri = builder.Configuration["Authentication:JwksUri"]
            ?? "http://localhost:8081/auth/realms/rsoi/protocol/openid-connect/certs";
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var keys = new JsonWebKeySet(await client.GetStringAsync(jwksUri)).GetSigningKeys();
        if (keys.Count == 0) throw new InvalidOperationException("JWKS has no signing keys");
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.RefreshOnIssuerKeyNotFound = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,
                ValidateIssuer = true,
                ValidIssuer = issuer,
                ValidateAudience = true,
                ValidAudience = "booking-api",
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                ClockSkew = TimeSpan.Zero,
                NameClaimType = "preferred_username"
            };
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (string.IsNullOrWhiteSpace(context.Principal?.Identity?.Name))
                        context.Fail("Token must contain preferred_username");
                    return Task.CompletedTask;
                }
            };
        });
        builder.Services.AddAuthorization();
    }

    public static void UseBookingAuthentication(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            if ((path.StartsWithSegments("/api") || path.StartsWithSegments("/internal"))
                && path != "/api/v1/authorize" && path != "/api/v1/callback"
                && context.User.Identity?.IsAuthenticated != true)
            {
                await context.ChallengeAsync();
                return;
            }
            await next(context);
        });
        app.UseAuthorization();
    }
}
