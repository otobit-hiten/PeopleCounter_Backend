namespace PeopleCounter_Backend.Services
{
    public class DataRetentionBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DataRetentionBackgroundService> _logger;

        public DataRetentionBackgroundService(IServiceScopeFactory scopeFactory, ILogger<DataRetentionBackgroundService> logger)
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
                    var retentionService = scope.ServiceProvider.GetRequiredService<DataRetentionService>();

                    _logger.LogInformation("Starting data retention job.");
                    await retentionService.MoveOldDataToArchiveAsync(stoppingToken);
                    _logger.LogInformation("Data retention job completed.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Data retention job failed.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break; 
                }
            }
        }
    }
}
