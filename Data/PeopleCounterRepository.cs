



using Microsoft.Data.SqlClient;
using PeopleCounter_Backend.Models;
using System.Data;
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




        public async Task<List<string>> GetAllDevicesAsync()
        {
            const string sql = @"
                SELECT DISTINCT device_id
                FROM people_counter_log
                ORDER BY device_id";

            var list = new List<string>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            await conn.OpenAsync();
            using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
                list.Add(r.GetString(0));

            return list;
        }

        public async Task<List<string>> GetAllLocationAsync()
        {
            const string sql = @"
                SELECT DISTINCT location
                 FROM people_counter_log
                ORDER BY location";

            var list = new List<string>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            await conn.OpenAsync();
            using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
                list.Add(r.GetString(0));

            return list;
        }






        public async Task ResetDevice(string deviceId)
        {

            var sqlGetLatestRecord = @"
                SELECT TOP 1
                    in_count,
                    out_count,
                    event_time
                FROM dbo.people_counter_log
                WHERE device_id = @deviceId
                ORDER BY event_time DESC, id DESC;
            ";

            var sqlInsertReset = @"
                INSERT INTO dbo.people_counter_resets
                (device_id, reset_time, reset_in_count, reset_out_count)
                VALUES
                (@deviceId, @resetTime, @resetIn, @resetOut);";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            int inCount;
            int outCount;
            DateTime eventTime;

            using (var cmd = new SqlCommand(sqlGetLatestRecord, conn))
            {
                cmd.Parameters.AddWithValue("@deviceId", deviceId);

                using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync())
                    throw new InvalidOperationException("No data found for device");

                inCount = reader.GetInt32(0);
                outCount = reader.GetInt32(1);
                eventTime = reader.GetDateTime(2);
            }

            using (var cmd = new SqlCommand(sqlInsertReset, conn))
            {
                cmd.Parameters.AddWithValue("@deviceId", deviceId);
                cmd.Parameters.AddWithValue("@resetTime", eventTime);
                cmd.Parameters.AddWithValue("@resetIn", inCount);
                cmd.Parameters.AddWithValue("@resetOut", outCount);

                await cmd.ExecuteNonQueryAsync();
            }
        }

        public async Task ResetAllDevicesByBuildingAsync(string building)
        {
            const string sqlGetDevices = @"
                        SELECT DISTINCT device_id
                        FROM people_counter_log
                WHERE location = @building;";

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var deviceIds = new List<string>();

            using (var cmd = new SqlCommand(sqlGetDevices, conn))
            {
                cmd.Parameters.AddWithValue("@building", building);

                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    deviceIds.Add(reader.GetString(0));
                }
            }

            if (deviceIds.Count == 0)
                return;

            foreach (var deviceId in deviceIds)
            {
                await ResetDevice(deviceId);
            }
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

        public async Task InsertDataAsync(IEnumerable<PeopleCounter> records)
        {
            var recordList = records.ToList();
            if (recordList.Count == 0)
            {
                return;
            }

            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var dataTable = new DataTable();

            dataTable.Columns.Add("device_id", typeof(string));
            dataTable.Columns.Add("location", typeof(string));
            dataTable.Columns.Add("sublocation", typeof(string));
            dataTable.Columns.Add("in_count", typeof(int));
            dataTable.Columns.Add("out_count", typeof(int));
            dataTable.Columns.Add("capacity", typeof(int));
            dataTable.Columns.Add("event_time", typeof(DateTime));


            foreach (var item in recordList)
            {
                dataTable.Rows.Add(
                    item.DeviceId, item.Location ?? (object)DBNull.Value, item.SubLocation ?? (object)DBNull.Value,
                    item.InCount, item.OutCount, item.Capacity, item.EventTime);
            }
            using var bulkCopy = new SqlBulkCopy(conn)
            {
                DestinationTableName = "people_counter_log",
                BatchSize = 1000,
                BulkCopyTimeout = 60,
                EnableStreaming = true
            };

            bulkCopy.ColumnMappings.Add("device_id", "device_id");
            bulkCopy.ColumnMappings.Add("location", "location");
            bulkCopy.ColumnMappings.Add("sublocation", "sublocation");
            bulkCopy.ColumnMappings.Add("in_count", "in_count");
            bulkCopy.ColumnMappings.Add("out_count", "out_count");
            bulkCopy.ColumnMappings.Add("capacity", "capacity");
            bulkCopy.ColumnMappings.Add("event_time", "event_time");

            await bulkCopy.WriteToServerAsync(dataTable);
        }


        public async Task<List<PeopleCounter>> GetLatestLogicalDeviceByIdsAysnc(List<string> deviceIds)
        {
            if (deviceIds == null || deviceIds.Count == 0)
            {
                return new List<PeopleCounter>();
            }

            const string sql = @"
WITH latest_reset AS (
    -- STEP 1: Get the most recent reset for each device
    SELECT
        device_id,
        reset_in_count,
        reset_out_count,
        ROW_NUMBER() OVER (
            PARTITION BY device_id      -- Separate numbering for each device
            ORDER BY reset_time DESC    -- Newest first
        ) AS rn
    FROM people_counter_resets
    WHERE device_id IN ({0})            -- Filter: only requested devices
),
latest_log AS (
    -- STEP 2: Get the most recent log entry for each device
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY device_id
               ORDER BY created_at DESC, id DESC
           ) AS rn
    FROM people_counter_log
    WHERE device_id IN ({0})            -- Filter: only requested devices
)
SELECT
    l.device_id,
    l.location,
    l.sublocation,
    l.event_time,

    -- STEP 3: Calculate logical IN count (subtract reset if exists)
    CASE
        WHEN r.reset_in_count IS NULL THEN l.in_count
        WHEN l.in_count < r.reset_in_count THEN l.in_count
        ELSE l.in_count - r.reset_in_count
    END AS logical_in,

    -- STEP 4: Calculate logical OUT count
    CASE
        WHEN r.reset_out_count IS NULL THEN l.out_count
        WHEN l.out_count < r.reset_out_count THEN l.out_count
        ELSE l.out_count - r.reset_out_count
    END AS logical_out,

    -- STEP 5: Calculate people inside (IN - OUT), minimum 0
    CASE
        WHEN
          (
            (CASE
                WHEN r.reset_in_count IS NULL THEN l.in_count
                WHEN l.in_count < r.reset_in_count THEN l.in_count
                ELSE l.in_count - r.reset_in_count
             END)
          -
            (CASE
                WHEN r.reset_out_count IS NULL THEN l.out_count
                WHEN l.out_count < r.reset_out_count THEN l.out_count
                ELSE l.out_count - r.reset_out_count
             END)
          ) < 0
        THEN 0
        ELSE
          (
            (CASE
                WHEN r.reset_in_count IS NULL THEN l.in_count
                WHEN l.in_count < r.reset_in_count THEN l.in_count
                ELSE l.in_count - r.reset_in_count
             END)
          -
            (CASE
                WHEN r.reset_out_count IS NULL THEN l.out_count
                WHEN l.out_count < r.reset_out_count THEN l.out_count
                ELSE l.out_count - r.reset_out_count
             END)
          )
    END AS inside,

    l.capacity

FROM latest_log l
LEFT JOIN latest_reset r
    ON l.device_id = r.device_id
    AND r.rn = 1                       
WHERE l.rn = 1;                         
";

            var result = new List<PeopleCounter>();
            using var conn = new SqlConnection(_connectionString);
            await conn.OpenAsync();

            var parameters = new List<string>();

            for (int i = 0; i < deviceIds.Count; i++)
            {
                parameters.Add($"@DeviceId{i}");
            }
            var parameterList = string.Join(",", parameters);
            var finalSql = string.Format(sql, parameterList);
            using var cmd = new SqlCommand(finalSql, conn);
            for (int i = 0; i < deviceIds.Count; i++)
            {
                cmd.Parameters.AddWithValue($"@DeviceId{i}", deviceIds[i]);
            }
            using var reader = await cmd.ExecuteReaderAsync();


            while (await reader.ReadAsync())
            {
                result.Add(new PeopleCounter
                {
                    DeviceId = reader.GetString(0),
                    Location = reader.GetString(1),
                    SubLocation = reader.IsDBNull(2) ? null : reader.GetString(2),
                    EventTime = reader.GetDateTime(3),
                    InCount = reader.GetInt32(4),
                    OutCount = reader.GetInt32(5),
                    Capacity = reader.GetInt32(7)
                });
            }

            return result;
        }


        public async Task<string> GetBuildingByDevice(string deviceId)
        {
            const string sql = @"
        SELECT TOP 1 location
        FROM people_counter_log
        WHERE device_id = @deviceId
        ORDER BY created_at DESC, id DESC;
    ";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@deviceId", deviceId);

            await conn.OpenAsync();

            var result = await cmd.ExecuteScalarAsync();

            if (result == null || result == DBNull.Value)
                throw new InvalidOperationException(
                    $"No building found for device '{deviceId}'");

            return (string)result;
        }

        public async Task<List<BuildingSummary>> GetBuildingSummaryAsync()
        {
            const string sql = @"
WITH latest_reset AS (
    -- Get latest reset for each device
    SELECT
        device_id,
        reset_in_count,
        reset_out_count,
        ROW_NUMBER() OVER (
            PARTITION BY device_id
            ORDER BY reset_time DESC
        ) AS rn
    FROM people_counter_resets
),
latest_log AS (
    -- Get latest log entry for each device
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY device_id
               ORDER BY created_at DESC, id DESC
           ) AS rn
    FROM people_counter_log
)
SELECT
    l.location AS Building,
    
    -- Aggregate: Total IN across all devices in this building
    SUM(
        CASE
            WHEN r.reset_in_count IS NULL THEN l.in_count
            WHEN l.in_count < r.reset_in_count THEN l.in_count
            ELSE l.in_count - r.reset_in_count
        END
    ) AS TotalIn,
    
    -- Aggregate: Total OUT across all devices in this building
    SUM(
        CASE
            WHEN r.reset_out_count IS NULL THEN l.out_count
            WHEN l.out_count < r.reset_out_count THEN l.out_count
            ELSE l.out_count - r.reset_out_count
        END
    ) AS TotalOut,
    
    -- Aggregate: Total Capacity across all devices in this building
    SUM(l.capacity) AS TotalCapacity

FROM latest_log l
LEFT JOIN latest_reset r
    ON l.device_id = r.device_id
    AND r.rn = 1
WHERE l.rn = 1              -- Only latest entries
GROUP BY l.location;        -- Group by building
";

            var result = new List<BuildingSummary>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                result.Add(new BuildingSummary(
                    Building: reader.GetString(0),
                    TotalIn: reader.GetInt32(1),
                    TotalOut: reader.GetInt32(2),
                    TotalCapacity: reader.GetInt32(3)
                ));
            }

            return result;
        }

        public async Task<List<PeopleCounter>> GetSensorsByBuildingAsync(string building)
        {
            var devices = await GetLatestLogicalDevicesAsync();

            return devices
                .Where(d => d.Location == building)
                .ToList();
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

        public async Task<List<PeopleCounter>> GetLatestLogicalDevicesAsync()
        {
            const string sql = @"
            WITH latest_reset AS (
                SELECT
                    device_id,
                    reset_in_count,
                    reset_out_count,
                    ROW_NUMBER() OVER (
                        PARTITION BY device_id
                        ORDER BY reset_time DESC
                    ) AS rn
                FROM people_counter_resets
            ),
            latest_log AS (
                SELECT *,
                       ROW_NUMBER() OVER (
                           PARTITION BY device_id
                           ORDER BY created_at DESC, id DESC
                       ) AS rn
                FROM people_counter_log
            )
            SELECT
                l.device_id,
                l.location,
                l.sublocation,
                l.event_time,

                -- LOGICAL TOTALS (RESET-AWARE)
                l.in_count  - ISNULL(r.reset_in_count, 0)  AS logical_in,
                l.out_count - ISNULL(r.reset_out_count, 0) AS logical_out,

                -- INSIDE (FINAL VALUE)
                (l.in_count  - ISNULL(r.reset_in_count, 0))
              - (l.out_count - ISNULL(r.reset_out_count, 0)) AS inside

            FROM latest_log l
            LEFT JOIN latest_reset r
                ON l.device_id = r.device_id
                AND r.rn = 1
            WHERE l.rn = 1;
            ";

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
                    Location = reader.GetString(1),
                    SubLocation = reader.IsDBNull(2) ? null : reader.GetString(2),
                    EventTime = reader.GetDateTime(3),

                    InCount = reader.GetInt32(4),
                    OutCount = reader.GetInt32(5),

                    Capacity = reader.GetInt32(6)
                });
            }

            return result;
        }

        public async Task<PeopleCounter?> GetLatestLogicalDeviceAsync(string deviceId)
        {
            const string sql = @"
    WITH latest_reset AS (
        SELECT
            reset_in_count,
            reset_out_count
        FROM people_counter_resets
        WHERE device_id = @deviceId
        ORDER BY created_at DESC
        OFFSET 0 ROWS FETCH NEXT 1 ROWS ONLY
    ),
    latest_log AS (
        SELECT TOP 1 *
        FROM people_counter_log
        WHERE device_id = @deviceId
        ORDER BY created_at DESC, id DESC
    )
    SELECT
        l.device_id,
        l.location,
        l.sublocation,
        l.event_time,

        CASE
            WHEN r.reset_in_count IS NULL THEN l.in_count
            WHEN l.in_count < r.reset_in_count THEN l.in_count
            ELSE l.in_count - r.reset_in_count
        END AS logical_in,

        CASE
            WHEN r.reset_out_count IS NULL THEN l.out_count
            WHEN l.out_count < r.reset_out_count THEN l.out_count
            ELSE l.out_count - r.reset_out_count
        END AS logical_out,

        CASE
            WHEN
              (
                (CASE
                    WHEN r.reset_in_count IS NULL THEN l.in_count
                    WHEN l.in_count < r.reset_in_count THEN l.in_count
                    ELSE l.in_count - r.reset_in_count
                 END)
              -
                (CASE
                    WHEN r.reset_out_count IS NULL THEN l.out_count
                    WHEN l.out_count < r.reset_out_count THEN l.out_count
                    ELSE l.out_count - r.reset_out_count
                 END)
              ) < 0
            THEN 0
            ELSE
              (
                (CASE
                    WHEN r.reset_in_count IS NULL THEN l.in_count
                    WHEN l.in_count < r.reset_in_count THEN l.in_count
                    ELSE l.in_count - r.reset_in_count
                 END)
              -
                (CASE
                    WHEN r.reset_out_count IS NULL THEN l.out_count
                    WHEN l.out_count < r.reset_out_count THEN l.out_count
                    ELSE l.out_count - r.reset_out_count
                 END)
              )
        END AS inside,

        l.capacity
    FROM latest_log l
    LEFT JOIN latest_reset r ON 1 = 1;
    ";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@deviceId", deviceId);

            await conn.OpenAsync();
            using var reader = await cmd.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
                return null;

            return new PeopleCounter
            {
                DeviceId = reader.GetString(0),
                Location = reader.GetString(1),
                SubLocation = reader.IsDBNull(2) ? null : reader.GetString(2),
                EventTime = reader.GetDateTime(3),
                InCount = reader.GetInt32(4),
                OutCount = reader.GetInt32(5),
                Capacity = reader.GetInt32(7)
            };
        }









        public async Task<List<SensorTrendPointDto>> GetSensorTrendAsync(
            string deviceId,
            DateTime from,
            DateTime to,
            string bucket)
        {
            var result = new List<SensorTrendPointDto>();

            //            var sql = @"WITH reset_adjusted AS (
            //    SELECT
            //        l.device_id,
            //        l.event_time,

            //        l.in_count
            //        - ISNULL((
            //            SELECT TOP 1 r.reset_in_count
            //            FROM people_counter_resets r
            //            WHERE r.device_id = l.device_id
            //              AND r.reset_time <= l.event_time
            //            ORDER BY r.reset_time DESC
            //        ), 0) AS adj_in,

            //        l.out_count
            //        - ISNULL((
            //            SELECT TOP 1 r.reset_out_count
            //            FROM people_counter_resets r
            //            WHERE r.device_id = l.device_id
            //              AND r.reset_time <= l.event_time
            //            ORDER BY r.reset_time DESC
            //        ), 0) AS adj_out

            //    FROM people_counter_log l
            //    WHERE l.device_id = @deviceId
            //      AND l.event_time BETWEEN @fromDate AND @toDate
            //),

            //bucketed AS (
            //    SELECT
            //        CASE
            //            WHEN @bucket = 'hour'
            //                THEN DATEADD(hour, DATEDIFF(hour, 0, event_time), 0)
            //            WHEN @bucket = 'day'
            //                THEN CAST(event_time AS date)
            //            WHEN @bucket = 'month'
            //                THEN DATEFROMPARTS(YEAR(event_time), MONTH(event_time), 1)
            //        END AS bucket_time,

            //        MAX(adj_in)  AS total_in,
            //        MAX(adj_out) AS total_out

            //    FROM reset_adjusted
            //    GROUP BY
            //        CASE
            //            WHEN @bucket = 'hour'
            //                THEN DATEADD(hour, DATEDIFF(hour, 0, event_time), 0)
            //            WHEN @bucket = 'day'
            //                THEN CAST(event_time AS date)
            //            WHEN @bucket = 'month'
            //                THEN DATEFROMPARTS(YEAR(event_time), MONTH(event_time), 1)
            //        END
            //)

            //SELECT
            //    bucket_time AS [time],
            //    total_in,
            //    total_out
            //FROM bucketed
            //ORDER BY bucket_time;
            //";

            var sql = @"WITH reset_adjusted AS (
                SELECT
                    l.device_id,
                    l.event_time,

                    l.in_count
                    - ISNULL((
                        SELECT TOP 1 r.reset_in_count
                        FROM people_counter_resets r
                        WHERE r.device_id = l.device_id
                          AND r.reset_time <= l.event_time
                        ORDER BY r.reset_time DESC
                    ), 0) AS adj_in,

                    l.out_count
                    - ISNULL((
                        SELECT TOP 1 r.reset_out_count
                        FROM people_counter_resets r
                        WHERE r.device_id = l.device_id
                          AND r.reset_time <= l.event_time
                        ORDER BY r.reset_time DESC
                    ), 0) AS adj_out
                FROM people_counter_log l
                WHERE l.device_id = @deviceId
                  AND l.event_time BETWEEN @fromDate AND @toDate
            ),

            bucketed AS (
                SELECT
                    DATEADD(hour, DATEDIFF(hour, 0, event_time), 0) AS bucket_time,
                    MAX(adj_in)  AS cum_in,
                    MAX(adj_out) AS cum_out
                FROM reset_adjusted
                GROUP BY DATEADD(hour, DATEDIFF(hour, 0, event_time), 0)
            ),

            diffs AS (
                SELECT
                    bucket_time,
                    cum_in  - LAG(cum_in, 1, cum_in)   OVER (ORDER BY bucket_time) AS hourly_in,
                    cum_out - LAG(cum_out, 1, cum_out) OVER (ORDER BY bucket_time) AS hourly_out
                FROM bucketed
            )

            SELECT
                bucket_time AS [time],
                hourly_in   AS [in],
                hourly_out  AS [out]
            FROM diffs
            ORDER BY bucket_time;
            ";
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@deviceId", deviceId);
            cmd.Parameters.AddWithValue("@fromDate", from);
            cmd.Parameters.AddWithValue("@toDate", to);
            cmd.Parameters.AddWithValue("@bucket", bucket);

            await conn.OpenAsync();
            using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
            {
                result.Add(new SensorTrendPointDto
                {
                    Time = r.GetDateTime(0),
                    In = Convert.ToInt32(r.GetValue(1)),
                    Out = Convert.ToInt32(r.GetValue(2))
                });
            }

            return result;
        }


        public async Task<List<SensorTrendPointDto>> GetLocationTrendAsync(
                string location,
                DateTime from,
                DateTime to,
                string bucket)
        {
            var result = new List<SensorTrendPointDto>();

            var sql = @"WITH reset_adjusted AS (
                SELECT
                    l.device_id,
                    l.location,
                    l.event_time,

                    l.in_count -
                    ISNULL((
                        SELECT TOP 1 r.reset_in_count
                        FROM people_counter_resets r
                        WHERE r.device_id = l.device_id
                          AND r.reset_time <= l.event_time
                        ORDER BY r.reset_time DESC
                    ), 0) AS adj_in,

                    l.out_count -
                    ISNULL((
                        SELECT TOP 1 r.reset_out_count
                        FROM people_counter_resets r
                        WHERE r.device_id = l.device_id
                          AND r.reset_time <= l.event_time
                        ORDER BY r.reset_time DESC
                    ), 0) AS adj_out
                FROM people_counter_log l
                WHERE l.location = @location
                  AND l.event_time BETWEEN @fromDate AND @toDate
            ),

            bucketed_last AS (
                SELECT
                    device_id,
                    location,

                    CASE
                        WHEN @bucket = 'hour'  THEN DATEADD(hour, DATEDIFF(hour, 0, event_time), 0)
                        WHEN @bucket = 'day'   THEN CAST(event_time AS date)
                        WHEN @bucket = 'month' THEN DATEFROMPARTS(YEAR(event_time), MONTH(event_time), 1)
                    END AS bucket_time,

                    MAX(adj_in)  AS cum_in,
                    MAX(adj_out) AS cum_out
                FROM reset_adjusted
                GROUP BY
                    device_id,
                    location,
                    CASE
                        WHEN @bucket = 'hour'  THEN DATEADD(hour, DATEDIFF(hour, 0, event_time), 0)
                        WHEN @bucket = 'day'   THEN CAST(event_time AS date)
                        WHEN @bucket = 'month' THEN DATEFROMPARTS(YEAR(event_time), MONTH(event_time), 1)
                    END
            ),

            per_device_diffs AS (
                SELECT
                    device_id,
                    location,
                    bucket_time,

                    cum_in  - LAG(cum_in, 1, cum_in)   OVER (PARTITION BY device_id ORDER BY bucket_time) AS device_in,
                    cum_out - LAG(cum_out, 1, cum_out) OVER (PARTITION BY device_id ORDER BY bucket_time) AS device_out
                FROM bucketed_last
            )

            SELECT
                bucket_time AS [time],
                SUM(device_in)  AS total_in,
                SUM(device_out) AS total_out
            FROM per_device_diffs
            GROUP BY bucket_time
            ORDER BY bucket_time;";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("@location", location);
            cmd.Parameters.AddWithValue("@fromDate", from);
            cmd.Parameters.AddWithValue("@toDate", to);
            cmd.Parameters.AddWithValue("@bucket", bucket);

            await conn.OpenAsync();
            using var r = await cmd.ExecuteReaderAsync();

            while (await r.ReadAsync())
            {
                result.Add(new SensorTrendPointDto
                {
                    Time = r.GetDateTime(0),
                    In = Convert.ToInt32(r.GetValue(1)),
                    Out = Convert.ToInt32(r.GetValue(2))
                });
            }

            return result;
        }
    }
}
