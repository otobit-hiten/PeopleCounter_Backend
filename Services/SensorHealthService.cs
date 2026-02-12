using PeopleCounter_Backend.Models;
using System.Net.NetworkInformation;

namespace PeopleCounter_Backend.Services
{
    public class SensorHealthService
    {
        private readonly SensorRepository _repo;
        private readonly SensorCacheService _sensorCache;
        private readonly ILogger<SensorHealthService> _logger;

        public SensorHealthService(
            SensorRepository repo,
            SensorCacheService sensorCache,
            ILogger<SensorHealthService> logger)
        {
            _repo = repo;
            _sensorCache = sensorCache;
            _logger = logger;
        }

        public async Task CheckAsync()
        {
            await _sensorCache.InitializeAsync();
            var sensors = _sensorCache.GetAll();

            _logger.LogInformation("Starting health check for {Count} sensors", sensors.Count);

            int onlineCount = 0;
            int offlineCount = 0;

            foreach (var sensor in sensors)
            {
                bool online = await PingAsync(sensor.Device, sensor.IpAddress);
                await _repo.UpdateStatusAsync(sensor.Id, online);
                _sensorCache.UpdateStatus(sensor.Device, online,
                    online ? DateTime.Now : null);

                if (online) onlineCount++;
                else offlineCount++;
            }

            _logger.LogInformation(
                "Health check complete: {Online} online, {Offline} offline out of {Total} sensors",
                onlineCount, offlineCount, sensors.Count);
        }

        private async Task<bool> PingAsync(string device, string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                _logger.LogWarning("Sensor {Device} has no IP address — skipping ping", device);
                return false;
            }

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 2000);

                if (reply.Status == IPStatus.Success)
                {
                    _logger.LogInformation(
                        "Sensor {Device} ({Ip}) is ONLINE — {RoundTrip}ms",
                        device, ip, reply.RoundtripTime);
                    return true;
                }
                else
                {
                    _logger.LogWarning(
                        "Sensor {Device} ({Ip}) is OFFLINE — status: {Status}",
                        device, ip, reply.Status);
                    return false;
                }
            }
            catch (PingException ex)
            {
                _logger.LogWarning(
                    "Sensor {Device} ({Ip}) ping failed: {Message}",
                    device, ip, ex.InnerException?.Message ?? ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error pinging sensor {Device} ({Ip})",
                    device, ip);
                return false;
            }
        }
    }
}