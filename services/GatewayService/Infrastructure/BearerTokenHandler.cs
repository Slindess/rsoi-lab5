using System.Net.Http.Headers;

namespace GatewayService.Infrastructure;

public static class RetryTokenContext
{
    private static readonly AsyncLocal<string?> Token = new();
    public static string? Current { get => Token.Value; set => Token.Value = value; }
}

public sealed class BearerTokenHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var authorization = RetryTokenContext.Current
            ?? accessor.HttpContext?.Request.Headers.Authorization.ToString();
        if (AuthenticationHeaderValue.TryParse(authorization, out var header)
            && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            request.Headers.Authorization = header;
        return base.SendAsync(request, ct);
    }
}
