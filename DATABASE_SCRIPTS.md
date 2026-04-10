# PeopleCounter_Backend — Database Scripts

> Use these scripts on the client machine SQL Server Express instance.
> All scripts are safe to re-run — `IF NOT EXISTS` guards prevent duplicate errors.

---

## SCRIPT 1 — Drop All Custom Indexes

Run this to wipe all custom indexes (e.g. before a clean reinstall or migration).
Does **not** drop Primary Keys or system-generated unique constraints.

```sql
-- Drops all named indexes (safe — skips PKs and system constraints)
DROP INDEX IF EXISTS IX_pcl_device_created          ON dbo.people_counter_log;
DROP INDEX IF EXISTS IX_pcl_device_event            ON dbo.people_counter_log;
DROP INDEX IF EXISTS IX_pcl_event_time              ON dbo.people_counter_log;
DROP INDEX IF EXISTS IX_pcl_location_created        ON dbo.people_counter_log;
DROP INDEX IF EXISTS IX_pcla_device_event           ON dbo.people_counter_log_archive;
DROP INDEX IF EXISTS IX_pcr_device_reset_time       ON dbo.people_counter_resets;
DROP INDEX IF EXISTS IX_userroles_userid            ON dbo.UserRoles;
DROP INDEX IF EXISTS IX_users_username              ON dbo.Users;

PRINT 'All custom indexes dropped.';
```

---

## SCRIPT 2 — Create Archive Table + All Indexes

Run this on a fresh database or after running Script 1.
Creates the archive table if missing, then applies all indexes.

```sql
-- ============================================================
-- STEP 1: Create archive table if it doesn't exist
-- ============================================================
IF NOT EXISTS (
    SELECT 1 FROM sys.tables WHERE name = 'people_counter_log_archive'
)
BEGIN
    CREATE TABLE dbo.people_counter_log_archive (
        id          BIGINT          NOT NULL,
        device_id   NVARCHAR(100)   NOT NULL,
        location    NVARCHAR(200)   NULL,
        sublocation NVARCHAR(200)   NULL,
        in_count    INT             NOT NULL,
        out_count   INT             NOT NULL,
        capacity    INT             NOT NULL DEFAULT 0,
        event_time  DATETIME        NOT NULL,
        created_at  DATETIME        NOT NULL DEFAULT GETDATE(),
        CONSTRAINT PK_people_counter_log_archive PRIMARY KEY (id)
    );
    PRINT 'Created table: people_counter_log_archive';
END
ELSE
    PRINT 'Table already exists: people_counter_log_archive';

-- ============================================================
-- STEP 2: people_counter_log indexes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pcl_device_created' AND object_id = OBJECT_ID('dbo.people_counter_log'))
BEGIN
    CREATE INDEX IX_pcl_device_created
        ON dbo.people_counter_log (device_id, created_at DESC)
        INCLUDE (in_count, out_count, location, sublocation, event_time, capacity);
    PRINT 'Created: IX_pcl_device_created';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pcl_device_event' AND object_id = OBJECT_ID('dbo.people_counter_log'))
BEGIN
    CREATE INDEX IX_pcl_device_event
        ON dbo.people_counter_log (device_id, event_time DESC)
        INCLUDE (in_count, out_count, location, sublocation);
    PRINT 'Created: IX_pcl_device_event';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pcl_event_time' AND object_id = OBJECT_ID('dbo.people_counter_log'))
BEGIN
    CREATE INDEX IX_pcl_event_time
        ON dbo.people_counter_log (event_time ASC)
        INCLUDE (id, device_id, location, sublocation, in_count, out_count, capacity, created_at);
    PRINT 'Created: IX_pcl_event_time';
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pcl_location_created' AND object_id = OBJECT_ID('dbo.people_counter_log'))
BEGIN
    CREATE INDEX IX_pcl_location_created
        ON dbo.people_counter_log (location, created_at DESC)
        INCLUDE (device_id, in_count, out_count, sublocation, event_time, capacity);
    PRINT 'Created: IX_pcl_location_created';
END

-- ============================================================
-- STEP 3: people_counter_log_archive indexes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pcla_device_event' AND object_id = OBJECT_ID('dbo.people_counter_log_archive'))
BEGIN
    CREATE INDEX IX_pcla_device_event
        ON dbo.people_counter_log_archive (device_id, event_time DESC)
        INCLUDE (in_count, out_count, location, sublocation);
    PRINT 'Created: IX_pcla_device_event';
END

-- ============================================================
-- STEP 4: people_counter_resets indexes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_pcr_device_reset_time' AND object_id = OBJECT_ID('dbo.people_counter_resets'))
BEGIN
    CREATE INDEX IX_pcr_device_reset_time
        ON dbo.people_counter_resets (device_id, reset_time DESC)
        INCLUDE (reset_in_count, reset_out_count);
    PRINT 'Created: IX_pcr_device_reset_time';
END

-- ============================================================
-- STEP 5: UserRoles indexes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_userroles_userid' AND object_id = OBJECT_ID('dbo.UserRoles'))
BEGIN
    CREATE INDEX IX_userroles_userid
        ON dbo.UserRoles (UserId);
    PRINT 'Created: IX_userroles_userid';
END

-- ============================================================
-- STEP 6: Users indexes
-- ============================================================
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_users_username' AND object_id = OBJECT_ID('dbo.Users'))
BEGIN
    CREATE UNIQUE INDEX IX_users_username
        ON dbo.Users (Username)
        WHERE IsActive = 1;
    PRINT 'Created: IX_users_username';
END

PRINT 'All indexes applied successfully.';
```

---

## SCRIPT 3 — Verify Indexes

Run after Script 2 to confirm all indexes are in place.

```sql
SELECT
    t.name                          AS TableName,
    i.name                          AS IndexName,
    i.type_desc                     AS IndexType,
    i.is_unique                     AS IsUnique,
    i.is_primary_key                AS IsPrimaryKey,
    STRING_AGG(c.name, ', ')
        WITHIN GROUP (ORDER BY ic.key_ordinal) AS KeyColumns
FROM sys.indexes i
JOIN sys.tables t       ON i.object_id = t.object_id
JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
JOIN sys.columns c      ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE t.is_ms_shipped = 0
  AND ic.is_included_column = 0
GROUP BY t.name, i.name, i.type_desc, i.is_unique, i.is_primary_key
ORDER BY t.name, i.name;
```

---

## Expected Index State After Script 2

| Table | Index | Type | Purpose |
|-------|-------|------|---------|
| `people_counter_log` | `PK__people_c__...` | Clustered PK | Row lookup by `id` |
| `people_counter_log` | `IX_pcl_device_created` | Nonclustered | `GetBuildingSummary`, `GetSensorsByBuilding`, `GetLatestLogicalDeviceByIds` |
| `people_counter_log` | `IX_pcl_device_event` | Nonclustered | Trend queries, `GetBuildingByDevice` |
| `people_counter_log` | `IX_pcl_event_time` | Nonclustered | `DataRetentionService` archive/delete batches |
| `people_counter_log` | `IX_pcl_location_created` | Nonclustered | `GetSensorsByBuilding WHERE location = @building` |
| `people_counter_log_archive` | `PK_people_counter_log_archive` | Clustered PK | `NOT EXISTS` lookup in DataRetention |
| `people_counter_log_archive` | `IX_pcla_device_event` | Nonclustered | Trend queries (UNION with live table) |
| `people_counter_resets` | `PK__people_c__...` | Clustered PK | Row lookup by `id` |
| `people_counter_resets` | `IX_pcr_device_reset_time` | Nonclustered | All CTE reset calculations |
| `Sensors` | `PK__Sensors__...` | Clustered PK | Row lookup by `Id` |
| `Sensors` | `UQ__Sensors__...` | Nonclustered Unique | `GetByDeviceAsync`, `InsertIfNotExistsAsync` |
| `UserRoles` | `PK__UserRole__...` | Clustered PK | Row lookup |
| `UserRoles` | `IX_userroles_userid` | Nonclustered | `GetUser` JOIN on `UserId` |
| `Users` | `PK__Users__...` | Clustered PK | Row lookup by `UserId` |
| `Users` | `IX_users_username` | Nonclustered Unique | `GetUser WHERE Username = @username AND IsActive = 1` |
| `Roles` | `PK__Roles__...` | Clustered PK | Row lookup |
| `Roles` | `UQ__Roles__...` | Nonclustered Unique | Role name uniqueness |

---

## Usage Guide

| Scenario | Run |
|----------|-----|
| Fresh client machine setup | Script 2 only |
| Rebuilding indexes after corruption | Script 1 → Script 2 |
| Verifying indexes are correct | Script 3 |
| Before migration / clean reinstall | Script 1 → Script 2 |
