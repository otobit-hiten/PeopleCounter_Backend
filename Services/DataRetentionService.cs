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
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            const int batchSize = 10000;
            int totalArchived = 0;
            int totalDeleted = 0;

            while (true)
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var transaction = conn.BeginTransaction();

                try
                {
                    // Archive one batch
                    var archiveSql = @"
                        INSERT INTO people_counter_log_archive (
                            id, device_id, location, sublocation, in_count, out_count, capacity, event_time, created_at
                        )
                        SELECT TOP (@batchSize)
                            id, device_id, location, sublocation, in_count, out_count, capacity, event_time, created_at
                        FROM people_counter_log
                        WHERE event_time < @monthStart
                          AND id NOT IN (SELECT id FROM people_counter_log_archive);";

                    using var archiveCmd = new SqlCommand(archiveSql, conn, transaction);
                    archiveCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    archiveCmd.Parameters.AddWithValue("@monthStart", monthStart);
                    int archived = await archiveCmd.ExecuteNonQueryAsync();
                    totalArchived += archived;

                    // Delete the same batch
                    var deleteSql = @"
                        DELETE TOP (@batchSize) FROM people_counter_log
                        WHERE event_time < @monthStart
                          AND id IN (SELECT id FROM people_counter_log_archive);";

                    using var deleteCmd = new SqlCommand(deleteSql, conn, transaction);
                    deleteCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    deleteCmd.Parameters.AddWithValue("@monthStart", monthStart);
                    int deleted = await deleteCmd.ExecuteNonQueryAsync();
                    totalDeleted += deleted;

                    await transaction.CommitAsync();

                    // No more rows to process
                    if (deleted == 0) break;

                    // Small pause between batches to avoid sustained lock pressure
                    await Task.Delay(500);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }
        }
    }
}
