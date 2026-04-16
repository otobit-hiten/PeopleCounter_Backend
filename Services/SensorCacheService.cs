using PeopleCounter_Backend.Models;
using System.Collections.Concurrent;

namespace PeopleCounter_Backend.Services
{
    public class SensorCacheService
    {
        private readonly ConcurrentDictionary<string, Sensor> _cache = new();
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<SensorCacheService> _logger;
        private volatile bool _initialized = false;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly SemaphoreSlim _insertLock = new(1, 1);

        public SensorCacheService(
            IServiceScopeFactory scopeFactory,
            ILogger<SensorCacheService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public async Task InitializeAsync()
        {
            if (_initialized) return;

            await _initLock.WaitAsync();
            try
            {
                if (_initialized) return;

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<SensorRepository>();
                var sensors = await repo.GetAllAsync();

                foreach (var sensor in sensors)
                {
                    _cache[sensor.Device] = sensor;
                }

                _initialized = true;
                _logger.LogInformation("Sensor cache initialized with {Count} sensors", sensors.Count);
            }
            catch
            {
                _cache.Clear();
                throw;
            }
            finally
            {
                _initLock.Release();
            }
        }

        public bool TryGetSensor(string deviceId, out Sensor? sensor)
        {
            return _cache.TryGetValue(deviceId, out sensor);
        }

        public IReadOnlyCollection<Sensor> GetAll() => _cache.Values.ToList();

        public async Task<Sensor?> EnsureSensorExistsAsync(string deviceId, string location, string ipAddress)
        {
            if (!_initialized) await InitializeAsync();

            if (_cache.TryGetValue(deviceId, out var existing))
                return existing;

            await _insertLock.WaitAsync();
            try
            {
                if (_cache.TryGetValue(deviceId, out existing))
                    return existing;

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<SensorRepository>();
                await repo.InsertIfNotExistsAsync(deviceId, location, ipAddress);
                var sensor = await repo.GetByDeviceAsync(deviceId);

                if (sensor != null)
                {
                    _cache[deviceId] = sensor;
                    _logger.LogDebug(
                        "New sensor discovered and cached: {DeviceId} at {Location} ({Ip})",
                        deviceId, location, ipAddress);
                }
                else
                {
                    _logger.LogWarning(
                        "Sensor {DeviceId} not found after insert — possible DB issue.", deviceId);
                }

                return sensor;
            }
            finally
            {
                _insertLock.Release();
            }
        }

        public void UpdateStatus(string deviceId, SensorStatus status, DateTime? lastSeen)
        {
            if (!_cache.TryGetValue(deviceId, out var existing)) return;

            var updated = new Sensor
            {
                Id        = existing.Id,
                Device    = existing.Device,
                Location  = existing.Location,
                IpAddress = existing.IpAddress,
                Status    = status,
                IsOnline  = status == SensorStatus.Online,
                LastSeen  = lastSeen ?? existing.LastSeen
            };

            _cache[deviceId] = updated;
        }
    }
}
