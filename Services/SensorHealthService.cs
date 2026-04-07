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

            var results = await Task.WhenAll(sensors.Select(CheckSensorAsync));

            int onlineCount  = results.Count(s => s == SensorStatus.Online);
            int idleCount    = results.Count(s => s == SensorStatus.Idle);
            int offlineCount = results.Count(s => s == SensorStatus.Offline);

            _logger.LogInformation(
                "Health check complete: {Online} online, {Idle} idle, {Offline} offline out of {Total} sensors",
                onlineCount, idleCount, offlineCount, sensors.Count);
        }

        private async Task<SensorStatus> CheckSensorAsync(Sensor sensor)
        {
            SensorStatus status;

            bool hasRecentData = await _repo.IsActiveRecentlyAsync(sensor.Device, minutes: 5);

            if (hasRecentData)
            {
                status = SensorStatus.Online;
                _logger.LogInformation("Sensor {Device} → ONLINE (recent data)", sensor.Device);
            }
            else
            {
                bool pingSuccess = await PingAsync(sensor.Device, sensor.IpAddress);

                if (pingSuccess)
                {
                    status = SensorStatus.Idle;
                    _logger.LogInformation(
                        "Sensor {Device} → IDLE (no recent data but ping OK)", sensor.Device);
                }
                else
                {
                    status = SensorStatus.Offline;
                    _logger.LogWarning(
                        "Sensor {Device} → OFFLINE (no data + ping failed)", sensor.Device);
                }
            }

            var lastSeen = await _repo.GetLastDataTimeAsync(sensor.Device);
            await _repo.UpdateStatusAsync(sensor.Id, status, lastSeen);
            _sensorCache.UpdateStatus(sensor.Device, status, lastSeen);

            return status;
        }

        private async Task<bool> PingAsync(string device, string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
            {
                _logger.LogWarning(
                    "Sensor {Device} has no IP address — skipping ping", device);
                return false;
            }

            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 2000);

                if (reply.Status == IPStatus.Success)
                {
                    _logger.LogInformation(
                        "Sensor {Device} ({Ip}) ping OK — {RoundTrip}ms",
                        device, ip, reply.RoundtripTime);
                    return true;
                }
                else
                {
                    _logger.LogWarning(
                        "Sensor {Device} ({Ip}) ping failed — status: {Status}",
                        device, ip, reply.Status);
                    return false;
                }
            }
            catch (PingException ex)
            {
                _logger.LogWarning(
                    "Sensor {Device} ({Ip}) ping exception: {Message}",
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