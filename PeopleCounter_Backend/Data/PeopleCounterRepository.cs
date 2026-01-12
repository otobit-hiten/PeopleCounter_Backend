using Microsoft.Data.SqlClient;
using PeopleCounter_Backend.Models;
using System.Net.NetworkInformation;

namespace PeopleCounter_Backend.Data
{
    public class PeopleCounterRepository
    {
        private readonly string _connectionString;

        public PeopleCounterRepository(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public async Task InsertAsync(IEnumerable<PeopleCounter> records)
        {
            var sql = @"
                INSERT INTO people_counter_log
                (device_id,location,sublocation, in_count, out_count, capacity, event_time)
                VALUES
                (@device,@location,@sublocation ,@in, @out, @capacity, @time)";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            foreach (var people in records)
            {
                using var cmd = new SqlCommand(sql, conn);

                cmd.Parameters.AddWithValue("@device", people.DeviceId);
                cmd.Parameters.AddWithValue("@location", people.Location);
                cmd.Parameters.AddWithValue("@sublocation", people.SubLocation);
                cmd.Parameters.AddWithValue("@in", people.InCount);
                cmd.Parameters.AddWithValue("@out", people.OutCount);
                cmd.Parameters.AddWithValue("@capacity", people.Capacity);
                cmd.Parameters.AddWithValue("@time", people.EventTime);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task<List<PeopleCounter>> GetLatestPerDeviceAsync()
        {
            var sql = @"
        SELECT
    device_id,
    location,
    in_count,
    out_count,
    capacity,
    event_time,
    sublocation
FROM (
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY device_id
               ORDER BY created_at DESC
           ) AS rn
    FROM people_counter_log
) t
WHERE rn = 1
ORDER BY device_id";

            var result = new List<PeopleCounter>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new PeopleCounter
                {
                    DeviceId = reader.GetString(0),
                    Location = reader.IsDBNull(1)
                            ? null
                            : reader.GetString(1),
                    InCount = reader.GetInt32(2),
                    OutCount = reader.GetInt32(3),
                    Capacity = reader.GetInt32(4),
                    EventTime = reader.GetDateTime(5),
                    SubLocation = reader.GetString(6)
                });
            }

            return result;
        }

        public async Task<List<BuildingSummary>> GetBuildingSummaryAsync()
        {
            var sql = @"
    SELECT
        location AS building,
        SUM(in_count) AS total_in,
        SUM(out_count) AS total_out,
        SUM(capacity) AS total_capacity
    FROM (
        SELECT *,
               ROW_NUMBER() OVER (
                   PARTITION BY device_id
                   ORDER BY created_at DESC
               ) rn
        FROM people_counter_log
    ) t
    WHERE rn = 1
    GROUP BY location";


            var result = new List<BuildingSummary>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            await conn.OpenAsync();

            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new BuildingSummary(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3)
                ));
            }
            return result;
        }


        public async Task<List<PeopleCounter>> GetSensorsByBuildingAsync(string building)
        {
            var sql = @"
    SELECT device_id, location, sublocation, in_count, out_count, capacity, event_time
    FROM (
        SELECT *,
               ROW_NUMBER() OVER (
                   PARTITION BY device_id
                   ORDER BY created_at DESC
               ) rn
        FROM people_counter_log
        WHERE location = @building
    ) t
    WHERE rn = 1";

            var result = new List<PeopleCounter>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@building", building);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new PeopleCounter
                {
                    DeviceId = reader.GetString(0),
                    Location = reader.GetString(1),
                    SubLocation = reader.GetString(2),
                    InCount = reader.GetInt32(3),
                    OutCount = reader.GetInt32(4),
                    Capacity = reader.GetInt32(5),
                    EventTime = reader.GetDateTime(6)
                });
            }

            return result;
        }

    }
}
