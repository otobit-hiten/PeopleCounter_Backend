using Microsoft.Data.SqlClient;

namespace PeopleCounter_Backend.Services
{
    public class DataRetentionService
    {
        private readonly string _connectionString;

        public DataRetentionService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public async Task MoveOldDataToArchiveAsync()
        {
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            using var transaction = conn.BeginTransaction();

            try
            {
                var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

                var sql = @"
                   INSERT INTO people_counter_log_archive (
                  id, device_id, count, event_time
                   )
                  SELECT id, device_id, count, event_time
                  FROM people_counter_log
                  WHERE event_time < @monthStart;
                  DELETE FROM people_counter_log
                  WHERE event_time < @monthStart;
                   ";

                using var cmd = new SqlCommand(sql, conn, transaction);
                cmd.Parameters.AddWithValue("@monthStart", monthStart);

                await cmd.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
