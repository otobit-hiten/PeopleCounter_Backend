namespace PeopleCounter_Backend.Services
{
    public class SensorHealthBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SensorHealthBackgroundService> _logger;

        public SensorHealthBackgroundService(IServiceScopeFactory scopeFactory, ILogger<SensorHealthBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<SensorHealthService>();
                    await service.CheckAsync();
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sensor health check failed unexpectedly.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                }
                catch (OperationCanceledException) { break; }
            }
        }
    }
}
