using Microsoft.Data.SqlClient;
using PeopleCounter_Backend.Models;
using System.Net;

namespace PeopleCounter_Backend.Services
{
    public class SensorRepository
    {
        private readonly IConfiguration _config;

        public SensorRepository(IConfiguration config)
        {
            _config = config;
        }

        public async Task<List<Sensor>> GetAllAsync()
        {
            var list = new List<Sensor>();
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                "SELECT Id, Device, IpAddress FROM Sensors", conn);

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new Sensor
                {
                    Id = reader.GetInt32(0),
                    Device = reader.GetString(1),
                    IpAddress = reader.GetString(2)
                });
            }

            return list;
        }

        public async Task UpdateStatusAsync(int id, bool online)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                @"UPDATE Sensors
              SET IsOnline = @online,
                  LastSeen = CASE WHEN @online = 1 THEN GETDATE() ELSE LastSeen END
              WHERE Id = @id", conn);

            cmd.Parameters.AddWithValue("@online", online);
            cmd.Parameters.AddWithValue("@id", id);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<Sensor?> GetByDeviceAsync(string device)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                @"SELECT Id, Device, Location, IpAddress, IsOnline, LastSeen
          FROM Sensors
          WHERE Device = @device", conn);

            cmd.Parameters.AddWithValue("@device", device);

            using var r = await cmd.ExecuteReaderAsync();
            if (!r.Read()) return null;

            return new Sensor
            {
                Id = r.GetInt32(0),
                Device = r.GetString(1),
                Location = r.GetString(2),
                IpAddress = r.IsDBNull(3) ? "" : r.GetString(3),
                IsOnline = r.GetBoolean(4),
                LastSeen = r.IsDBNull(5) ? null : r.GetDateTime(5)
            };
        }
        public async Task InsertIfNotExistsAsync(string device, string location, string ipAddress)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();
            var cmd = new SqlCommand(@"
        IF NOT EXISTS (SELECT 1 FROM Sensors WHERE Device = @device)
        INSERT INTO Sensors (Device, Location, IsOnline, IpAddress, LastSeen)
        VALUES (@device, @location, 1, @ip, GETDATE())", conn);
            cmd.Parameters.AddWithValue("@device", device);
            cmd.Parameters.AddWithValue("@location", location);
            cmd.Parameters.AddWithValue("@ip", ipAddress);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
