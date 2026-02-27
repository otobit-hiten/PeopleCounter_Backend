using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace PeopleCounter_Backend.Services
{
    public class MqttBackgroundService : BackgroundService
    {
        private readonly MqttService _mqttService;
        private readonly ILogger<MqttBackgroundService> _logger;

        public MqttBackgroundService(MqttService mqttService, ILogger<MqttBackgroundService> logger)
        {
            _mqttService = mqttService;
            _logger = logger;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Task.Yield();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_mqttService.IsConnected)
                    {
                        await _mqttService.Connect(stoppingToken);
                    }
                    await Task.Delay(5000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MQTT error. Retrying in 30 seconds...");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    catch (OperationCanceledException) { break; }
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await _mqttService.StopAsync();
            await base.StopAsync(cancellationToken);
        }
    }

}
