using System.Runtime.CompilerServices;

namespace GatewayService.Application;

using GatewayService.Infrastructure;

public sealed record LoyaltyRetryTask(string Username, string Authorization);

public sealed class LoyaltyRetryQueue
{
    private readonly List<LoyaltyRetryTask> _list = new();
    private readonly SemaphoreSlim _signal = new(0);
    private readonly object _gate = new();

    public ValueTask EnqueueAsync(LoyaltyRetryTask task, CancellationToken ct = default)
    {
        lock (_gate) _list.Add(task);
        _signal.Release();
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<LoyaltyRetryTask> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await _signal.WaitAsync(ct).ConfigureAwait(false);

            LoyaltyRetryTask task;
            lock (_gate)
            {
                if (_list.Count == 0) continue;
                task = _list[0];
                _list.RemoveAt(0);
            }

            yield return task;
        }
    }
}

public sealed class LoyaltyRetryWorker(
    LoyaltyRetryQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<LoyaltyRetryWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var task in queue.ReadAllAsync(stoppingToken))
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    RetryTokenContext.Current = task.Authorization;
                    using var scope = scopeFactory.CreateScope();
                    var clients = scope.ServiceProvider.GetRequiredService<IBookingClients>();
                    await clients.ChangeLoyaltyAsync(task.Username, false, stoppingToken);
                    break;
                }
                catch (HttpRequestException exception) when (exception.StatusCode is System.Net.HttpStatusCode.Unauthorized)
                {
                    logger.LogError("Loyalty retry for {Username} stopped: token expired or rejected", task.Username);
                    break;
                }
                catch (Exception exception) when (exception is DownstreamUnavailableException
                                                      or HttpRequestException
                                                      or TaskCanceledException)
                {
                    logger.LogWarning(exception,
                        "Could not decrease loyalty for {Username}; retrying in 10 seconds",
                        task.Username);
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                finally { RetryTokenContext.Current = null; }
            }
        }
    }
}
