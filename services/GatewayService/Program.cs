using Booking.Security;
using GatewayService.Api;
using GatewayService.Application;
using GatewayService.Infrastructure;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddHttpClient<IBookingClients, BookingHttpClients>(client =>
    client.Timeout = TimeSpan.FromSeconds(10)).AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddSingleton<InMemoryCircuitBreaker>();
builder.Services.AddSingleton<LoyaltyRetryQueue>();
builder.Services.AddHostedService<LoyaltyRetryWorker>();
builder.Services.AddScoped<BookingApplication>();

await builder.AddBookingAuthenticationAsync();
var app = builder.Build();
app.UseBookingAuthentication();
app.UseMiddleware<ExceptionMiddleware>();
app.MapGatewayEndpoints();
app.Run();
