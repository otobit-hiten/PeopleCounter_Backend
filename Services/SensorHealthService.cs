using PeopleCounter_Backend.Models;
using System.Net.NetworkInformation;

namespace PeopleCounter_Backend.Services
{
    public class SensorHealthService
    {
        private readonly SensorRepository _repo;

        public SensorHealthService(SensorRepository repo)
        {
            _repo = repo;
        }

        public async Task CheckAsync()
        {
            var sensors = await _repo.GetAllAsync();

            foreach (var sensor in sensors)
            {
                bool online = await PingAsync(sensor.IpAddress);
                await _repo.UpdateStatusAsync(sensor.Id, online);
            }
        }

        private async Task<bool> PingAsync(string ip)
        {
            try
            {
                using var ping = new Ping();
                var reply = await ping.SendPingAsync(ip, 2000);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}
