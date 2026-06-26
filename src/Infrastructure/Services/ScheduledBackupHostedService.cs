using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PakistanAccountingERP.Application.Interfaces.Services;
using PakistanAccountingERP.Domain.Enums;
using PakistanAccountingERP.Infrastructure.Options;

namespace PakistanAccountingERP.Infrastructure.Services;

public class ScheduledBackupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BackupOptions _options;
    private readonly ILogger<ScheduledBackupHostedService> _logger;

    public ScheduledBackupHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackupOptions> options,
        ILogger<ScheduledBackupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Scheduled backup hosted service is disabled.");
            return;
        }

        var intervalHours = Math.Max(1, _options.IntervalHours);
        var interval = TimeSpan.FromHours(intervalHours);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var backupService = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
                await backupService.RunBackupAsync(
                    JobRunType.Scheduled,
                    BackupDestination.Online,
                    stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Scheduled backup run failed.");
            }
        }
    }
}
