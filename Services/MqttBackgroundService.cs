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

            // Handles initial connection only.
            // All reconnection after a disconnect is owned by MqttService.HandleDisconnect
            // which uses exponential backoff and respects the stoppingToken.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _mqttService.Connect(stoppingToken);

                    // Connected — wait here until shutdown is requested.
                    // HandleDisconnect will manage any reconnects if the broker drops.
                    await Task.Delay(Timeout.Infinite, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Initial MQTT connection failed. Retrying in 30 seconds...");
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
