using Microsoft.Data.SqlClient;
using PeopleCounter_Backend.Models;

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
    }
}
