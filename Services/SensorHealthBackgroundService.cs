namespace PeopleCounter_Backend.Services
{
    public class SensorHealthBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;

        public SensorHealthBackgroundService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<SensorHealthService>();

                await service.CheckAsync();

                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
            }
        }
    }
}
