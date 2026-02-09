namespace PeopleCounter_Backend.Services
{
    public class DataRetentionBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public DataRetentionBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var retentionService =
                    scope.ServiceProvider.GetRequiredService<DataRetentionService>();

                try
                {
                    await retentionService.MoveOldDataToArchiveAsync();
                }
                catch
                {                   
                }

                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
        }
    }
}
