using Microsoft.Data.SqlClient;

namespace PeopleCounter_Backend.Services
{
    public class DataRetentionService
    {
        private readonly string _connectionString;
        private readonly ILogger<DataRetentionService> _logger;

        public DataRetentionService(IConfiguration config, ILogger<DataRetentionService> logger)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
            _logger = logger;
        }

        public async Task MoveOldDataToArchiveAsync()
        {
            var cutoffDate = DateTime.UtcNow.Date.AddDays(-15);
            _logger.LogInformation("Data retention cutoff: {CutoffDate}. Archiving records older than 15 days.", cutoffDate);

            const int batchSize = 10000;
            int totalMoved = 0;

            while (true)
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                using var transaction = conn.BeginTransaction();

                try
                {
                    var countSql = @"
                        SELECT COUNT(*) FROM (
                            SELECT TOP (@batchSize) p.id
                            FROM people_counter_log p
                            WHERE p.event_time < @cutoffDate
                              AND NOT EXISTS (
                                  SELECT 1 FROM people_counter_log_archive a WHERE a.id = p.id
                              )
                        ) x;";

                    using var countCmd = new SqlCommand(countSql, conn, transaction);
                    countCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    countCmd.Parameters.AddWithValue("@cutoffDate", cutoffDate);
                    countCmd.CommandTimeout = 60;

                    int rowsInBatch = (int)await countCmd.ExecuteScalarAsync();

                    if (rowsInBatch == 0)
                    {
                        await transaction.CommitAsync();
                        break;
                    }

                    var archiveSql = @"
                        INSERT INTO people_counter_log_archive (
                            id, device_id, location, sublocation, in_count, out_count, capacity, event_time, created_at
                        )
                        SELECT TOP (@batchSize)
                            p.id, p.device_id, p.location, p.sublocation,
                            p.in_count, p.out_count, p.capacity, p.event_time, p.created_at
                        FROM people_counter_log p
                        WHERE p.event_time < @cutoffDate
                          AND NOT EXISTS (
                              SELECT 1 FROM people_counter_log_archive a WHERE a.id = p.id
                          );";

                    using var archiveCmd = new SqlCommand(archiveSql, conn, transaction);
                    archiveCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    archiveCmd.Parameters.AddWithValue("@cutoffDate", cutoffDate);
                    archiveCmd.CommandTimeout = 120;
                    await archiveCmd.ExecuteNonQueryAsync();

                    var deleteSql = @"
                        DELETE TOP (@batchSize) p
                        FROM people_counter_log p
                        WHERE p.event_time < @cutoffDate
                          AND EXISTS (
                              SELECT 1 FROM people_counter_log_archive a WHERE a.id = p.id
                          );";

                    using var deleteCmd = new SqlCommand(deleteSql, conn, transaction);
                    deleteCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    deleteCmd.Parameters.AddWithValue("@cutoffDate", cutoffDate);
                    deleteCmd.CommandTimeout = 120;
                    await deleteCmd.ExecuteNonQueryAsync();

                    await transaction.CommitAsync();

                    totalMoved += rowsInBatch;
                    _logger.LogDebug("Batch complete — moved: {Rows}. Total moved: {Total}.", rowsInBatch, totalMoved);

                    await Task.Delay(500);
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }
            }

            _logger.LogInformation("Data retention finished. Total rows archived: {Total}.", totalMoved);
        }
    }
}
