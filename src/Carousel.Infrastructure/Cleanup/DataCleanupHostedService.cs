using Carousel.Application.Abstractions;
using Carousel.Application.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carousel.Infrastructure.Cleanup;

/// <summary>
/// Фоновый джоб retention-очистки: периодически вызывает <see cref="IDataCleanupService"/>.
/// Первый прогон — через StartupDelay (в Development 10 сек для наблюдаемости,
/// иначе 5 минут, чтобы не мешать старту/миграциям), далее — каждые IntervalHours.
/// </summary>
public sealed class DataCleanupHostedService : BackgroundService
{
    private static readonly TimeSpan DevStartupDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ErrorRetryDelay = TimeSpan.FromHours(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CleanupOptions _options;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DataCleanupHostedService> _logger;

    public DataCleanupHostedService(
        IServiceScopeFactory scopeFactory,
        CleanupOptions options,
        IWebHostEnvironment environment,
        ILogger<DataCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _environment = environment;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Data cleanup disabled (Cleanup:Enabled=false).");
            return;
        }

        var delay = _environment.IsDevelopment() ? DevStartupDelay : StartupDelay;
        _logger.LogInformation(
            "Data cleanup started: every {Interval}h, publications > {PubDays}d, orphan media > {MediaDays}d (first run in {Delay}).",
            _options.IntervalHours, _options.PublicationRetentionDays,
            _options.OrphanMediaRetentionDays, delay);

        try
        {
            await Task.Delay(delay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var cleanup = scope.ServiceProvider.GetRequiredService<IDataCleanupService>();
                    await cleanup.RunOnceAsync(stoppingToken);
                    await Task.Delay(TimeSpan.FromHours(_options.IntervalHours), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Data cleanup run failed; retry in {Retry}.", ErrorRetryDelay);
                    await Task.Delay(ErrorRetryDelay, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Остановка хоста — штатный выход.
        }
    }
}