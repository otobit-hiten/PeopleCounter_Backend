using Microsoft.Data.SqlClient;

namespace PeopleCounter_Backend.Services
{
    public class  DataRetentionService
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

                var sql = @"
                    DECLARE @monthStart DATETIME2 =
                    DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1);

                  -- Move previous months data
                  INSERT INTO people_counter_log_archive
                  SELECT *
                  FROM people_counter_log
                  WHERE event_time < @monthStart;

                  -- Delete moved data
                  DELETE FROM people_counter_log
                  WHERE event_time < @monthStart;
                  "; 

              using var cmd = new SqlCommand(sql, conn);
                   await cmd.ExecuteNonQueryAsync();
            }
        
    }
}
