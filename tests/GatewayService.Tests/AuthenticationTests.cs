using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Booking.Security;
using GatewayService.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace GatewayService.Tests;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task ApiAndInternalRoutesValidateJwtLocallyWhileHealthRemainsPublic()
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var keyBuilder = WebApplication.CreateBuilder();
        keyBuilder.WebHost.UseUrls("http://127.0.0.1:0");
        await using var keyServer = keyBuilder.Build();
        keyServer.MapGet("/certs", () => Results.Json(new { keys = new[] { new { kty = "RSA", kid = key.KeyId, alg = "RS256", use = "sig", n = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Modulus!), e = Base64UrlEncoder.Encode(rsa.ExportParameters(false).Exponent!) } } }));
        await keyServer.StartAsync();

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Issuer"] = "http://issuer/auth/realms/rsoi",
            ["Authentication:JwksUri"] = keyServer.Urls.Single() + "/certs"
        });
        await builder.AddBookingAuthenticationAsync();
        // All following requests must work without an available identity provider.
        await keyServer.StopAsync();
        await using var app = builder.Build();
        app.UseBookingAuthentication();
        app.MapGet("/api/v1/me", (HttpContext context) => Results.Text(context.User.Identity!.Name!));
        app.MapGet("/internal/example", () => Results.Ok());
        app.MapGet("/manage/health", () => Results.Ok());
        await app.StartAsync();
        using var http = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/manage/health")).StatusCode);
        foreach (var route in new[] { "/api/v1/me", "/internal/example", "/api/v1/missing" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync(route)).StatusCode);

        string Token(string issuer = "http://issuer/auth/realms/rsoi", string audience = "booking-api",
            bool expired = false, bool username = true, SecurityKey? signingKey = null) =>
            new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
                issuer, audience, username ? [new Claim("preferred_username", "alice")] : [],
                DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(expired ? -1 : 5),
                new SigningCredentials(signingKey ?? key, SecurityAlgorithms.RsaSha256)));

        using var otherRsa = RSA.Create(2048);
        foreach (var token in new[] { Token(expired: true), Token(issuer: "wrong"), Token(audience: "wrong"),
                     Token(username: false), Token(signingKey: new RsaSecurityKey(otherRsa)), "broken.token" })
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/v1/me")).StatusCode);
        }
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token());
        http.DefaultRequestHeaders.Add("X-User-Name", "mallory");
        Assert.Equal("alice", await http.GetStringAsync("/api/v1/me"));
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/internal/example")).StatusCode);
    }

    [Fact]
    public async Task DownstreamClientForwardsRequestAndBackgroundRetryTokens()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer incoming";
        var accessor = new HttpContextAccessor { HttpContext = context };
        using var handler = new BearerTokenHandler(accessor) { InnerHandler = new EchoAuthorizationHandler() };
        using var http = new HttpClient(handler);
        Assert.Equal("Bearer incoming", await http.GetStringAsync("http://downstream/internal/example"));
        try
        {
            RetryTokenContext.Current = "Bearer retry";
            Assert.Equal("Bearer retry", await http.GetStringAsync("http://downstream/internal/example"));
        }
        finally { RetryTokenContext.Current = null; }
    }

    private sealed class EchoAuthorizationHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(request.Headers.Authorization?.ToString() ?? "") });
    }
}
