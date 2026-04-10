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

        public async Task MoveOldDataToArchiveAsync(CancellationToken cancellationToken = default)
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
            const int batchSize = 10000;
            int totalArchived = 0;
            int totalDeleted = 0;

            // Open one connection for all batches instead of reopening per batch
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            while (true)
            {
                using var transaction = conn.BeginTransaction();

                try
                {
                    // Archive one batch — NOT EXISTS uses the index on archive.id
                    // instead of loading the full archive ID set into memory (NOT IN)
                    var archiveSql = @"
                        INSERT INTO people_counter_log_archive (
                            id, device_id, location, sublocation, in_count, out_count, capacity, event_time, created_at
                        )
                        SELECT TOP (@batchSize)
                            src.id, src.device_id, src.location, src.sublocation,
                            src.in_count, src.out_count, src.capacity, src.event_time, src.created_at
                        FROM people_counter_log src
                        WHERE src.event_time < @monthStart
                          AND NOT EXISTS (
                              SELECT 1 FROM people_counter_log_archive a WHERE a.id = src.id
                          );";

                    using var archiveCmd = new SqlCommand(archiveSql, conn, transaction);
                    archiveCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    archiveCmd.Parameters.AddWithValue("@monthStart", monthStart);
                    int archived = await archiveCmd.ExecuteNonQueryAsync(cancellationToken);
                    totalArchived += archived;

                    // Delete the same batch — EXISTS mirrors the NOT EXISTS above
                    var deleteSql = @"
                        DELETE TOP (@batchSize) FROM people_counter_log
                        WHERE event_time < @monthStart
                          AND EXISTS (
                              SELECT 1 FROM people_counter_log_archive a WHERE a.id = people_counter_log.id
                          );";

                    using var deleteCmd = new SqlCommand(deleteSql, conn, transaction);
                    deleteCmd.Parameters.AddWithValue("@batchSize", batchSize);
                    deleteCmd.Parameters.AddWithValue("@monthStart", monthStart);
                    int deleted = await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
                    totalDeleted += deleted;

                    await transaction.CommitAsync(cancellationToken);

                    // No more rows to process
                    if (deleted == 0) break;

                    // Small pause between batches to avoid sustained lock pressure.
                    // CancellationToken ensures clean shutdown during this delay.
                    await Task.Delay(500, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Batch archive failed after {Archived} archived, {Deleted} deleted. Rolling back batch.",
                        totalArchived, totalDeleted);
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }

            _logger.LogInformation(
                "Data retention complete: {Archived} rows archived, {Deleted} rows deleted.",
                totalArchived, totalDeleted);
        }
    }
}
