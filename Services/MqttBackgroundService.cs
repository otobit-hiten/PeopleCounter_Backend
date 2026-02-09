using System.Diagnostics;

namespace PeopleCounter_Backend.Services
{
    public class MqttBackgroundService : BackgroundService
    {
        private readonly MqttService _mqttService;

        public MqttBackgroundService(MqttService mqttService)
        {
            _mqttService = mqttService;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _mqttService.Connect(stoppingToken);
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(1000, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MQTT startup failed: {ex}");
            }

            await Task.Delay(Timeout.Infinite, stoppingToken);

        }
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _mqttService.StopAsync();
            await base.StopAsync(cancellationToken);
        }
    }

}
