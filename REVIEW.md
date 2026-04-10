# PeopleCounter_Backend — Full Production Review

> Reviewed: 2026-04-10  
> Reviewer: Claude Code (claude-sonnet-4-6)  
> Scope: All 21 C# source files, 7 config files  
> Status: **Read-only analysis — no changes made**

---

## FILE 1: `appsettings.json`

| Severity | Issue |
|----------|-------|
| **HIGH** | No connection pool settings. Under load, default pool settings (max 100) may queue up connections silently. Needs `Min Pool Size=5;Max Pool Size=200;Connection Timeout=30;Command Timeout=60;` |
| **HIGH** | `Urls` hardcoded to `192.168.88.17:8888` — if the client machine IP changes (DHCP), the service silently refuses all connections. |
| **MEDIUM** | MQTT credentials `emqx`/`emqx` are the broker defaults — anyone who knows the broker hostname can publish fake sensor data. |
| **MEDIUM** | No `appsettings.Production.json` to separate production-only overrides. |

---

## FILE 2: `Program.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `CookieSecurePolicy.None` — auth cookies are sent over plain HTTP. If the LAN is sniffed, session tokens are visible. Should be `SameSiteStrict` + `SecurePolicy.Always` (and use HTTPS termination at the NIC or a local reverse proxy). |
| **HIGH** | `SetIsOriginAllowed(_ => true)` with `AllowCredentials()` = CORS wildcard that also allows cookies. Any page on the LAN can make authenticated requests on behalf of logged-in users (CSRF-equivalent). Whitelist explicit origins. |
| **HIGH** | `app.UseWebSockets()` is called **after** `app.MapControllers()` and before `app.MapHub()`. WebSocket middleware must be in the pipeline *before* endpoint routing resolves. This works by accident in some versions but is incorrect ordering — move it above `app.UseAuthentication()`. |
| **HIGH** | `app.MapHealthChecks("/health")` is unauthenticated and returns SQL Server status. An attacker can probe `/health` to confirm the DB is running and identify the stack. Add `.RequireAuthorization()` or restrict to localhost. |
| **MEDIUM** | Swagger is only enabled in Development, but if `ASPNETCORE_ENVIRONMENT=Development` is set on the client machine it exposes full API docs publicly. |
| **MEDIUM** | No response compression (`UseResponseCompression`). SignalR payloads and JSON API responses will be uncompressed. |
| **MEDIUM** | `MqttMessageProcessor` is a **Singleton** that launches `Task.Run` in its **constructor**. If the channel processor crashes and needs to restart, there is no mechanism to do so. The `ContinueWith` only logs; it never restarts. |
| **LOW** | No rate limiting middleware. The `/auth/login` endpoint is wide open to brute force. |
| **LOW** | No `AddResponseCompression()` / Brotli / Gzip configured. |

---

## FILE 3: `Controllers/AuthController.cs`

| Severity | Issue |
|----------|-------|
| **HIGH** | Two different error messages: `"Invalid credentials"` (user not found) and `"Invalid Password"` (wrong password). This is a **username enumeration vulnerability** — an attacker can confirm which usernames exist. Use a single message: `"Invalid credentials"` for both cases. |
| **HIGH** | No rate limiting on `POST /auth/login`. A 1000 req/s brute force attack will succeed. |
| **MEDIUM** | No `[AllowAnonymous]` on `/login` and `/logout`. Being explicit is important for correctness if global auth policies are added later. |
| **MEDIUM** | `PasswordHasher<object>` with `null` context uses PBKDF2-SHA512, 100k iterations — acceptable but worth documenting so future maintainers don't switch to something weaker. |
| **LOW** | No model validation attributes (`[Required]`, `[MaxLength]`) on `LoginRequest` and `CreateUserRequest` — allows empty strings through to the DB query. |
| **LOW** | `CreateUser` validates `Username` and `Password` manually in the controller — this logic belongs in the model via data annotations. |

---

## FILE 4: `Controllers/DeviceController.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `GET /device/trend`, `GET /device/trendlocation`, `GET /device/list`, `GET /device/location`, `GET /device/daily-comparison`, `GET /device/status` have **no `[Authorize]` attribute**. Historical sensor data and building occupancy are fully public with zero authentication. |
| **HIGH** | `GetSensorStatuses()` calls `await _sensorCache.InitializeAsync()` on **every HTTP request**. The semaphore is acquired and released on every call even after initialization. Move initialization to a hosted service startup or app startup event. |
| **MEDIUM** | `ResetBuilding` has no error handling for the case where no devices are found in the building — it silently returns `Ok`. Also does not validate the `building` string for length or dangerous characters. |
| **MEDIUM** | `bucket.ToLower()` called 3+ times per request — use a single `var b = bucket.ToLower()` and reuse `b`. |
| **LOW** | No pagination on `GetListOfDevices()` or `GetListOfLocation()`. Will return unbounded results as sensors scale. |

---

## FILE 5: `Controllers/MqttController.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `[Authorize(Roles = "admin")]` uses lowercase `"admin"` but `AuthController` uses `[Authorize(Roles = "Admin")]` (uppercase). Role comparison in ASP.NET Core is **case-sensitive**. Users with role `"Admin"` cannot reach `POST /mqtt/publish`. This is a broken authorization gate. |
| **HIGH** | `GET /mqtt/buildings` and `GET /mqtt/building/{building}` have **no `[Authorize]`**. Building occupancy summary is publicly accessible. |
| **MEDIUM** | `PublishDto` is a `record` defined inside the controller file — should be in Models. |

---

## FILE 6: `Data/PeopleCounterRepository.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `GetLatestLogicalDeviceByIdsAysnc` builds SQL with `string.Format` and `{0}` appearing **twice** in the same query. There is **no upper bound** on `deviceIds.Count` — a large device fleet produces a very long IN clause which cannot use an index efficiently. |
| **HIGH** | `GetBuildingByDevice` queries `people_counter_log` (the hot fact table with millions of rows) to find a sensor's location. This should query the `Sensors` table directly since `Location` is stored there. |
| **HIGH** | `ResetAllDevicesInBuilding` calls `ResetDevice(deviceId)` in a `foreach` loop — each call opens a new `SqlConnection` and executes two SQL statements. For a building with 20 devices that is 40 round trips. Should be a single batched transaction. |
| **HIGH** | The trend queries (`GetSensorTrendAsync`, `GetLocationTrendAsync`) UNION `people_counter_log` with `people_counter_log_archive`. If the archive table is large, this is a full scan of both tables before the WHERE filter is applied. Needs covering indexes on both tables. |
| **MEDIUM** | `GetBuildingSummary` and `GetSensorsByBuilding` use `DATEADD(day, -30, GETDATE())` which uses SQL Server local time, but sensors may send UTC timestamps. Timezone inconsistency could mis-filter data. |
| **MEDIUM** | `_connectionString` nullable warning suppressed with no null check in constructor. If `DefaultConnection` is missing from config, this will NPE at first DB call rather than at startup. |
| **LOW** | `InsertDataAsync` creates a `DataTable` for bulk copy — the `BulkCopyTimeout = 60` is a hardcoded constant; should be configurable. |

---

## FILE 7: `Data/SensorRepository.cs`

| Severity | Issue |
|----------|-------|
| **HIGH** | **`SqlCommand` objects are not in `using` statements** in `GetAllAsync`, `GetByDeviceAsync`, `InsertIfNotExistsAsync`, `IsActiveRecentlyAsync`, `GetLastDataTimeAsync`. Commands hold unmanaged resources. Under high load this leaks handles. |
| **HIGH** | `GetByDeviceAsync` calls `r.Read()` (synchronous) instead of `await r.ReadAsync()`. This blocks the thread on a network I/O call in an async method. |
| **MEDIUM** | The class is in the `PeopleCounter_Backend.Services` namespace (line 8) but lives in the `Data/` folder. Namespace inconsistency will confuse tooling. |
| **LOW** | `IsActiveRecentlyAsync` queries `people_counter_log` with `COUNT(1)` and `created_at >= DATEADD(MINUTE, -@minutes, GETDATE())`. Without an index on `(device_id, created_at)` this is a table scan per sensor per health check — 20 table scans every 2 minutes. |

---

## FILE 8: `Data/UserRepository.cs`

| Severity | Issue |
|----------|-------|
| **HIGH** | `tx.Commit()` and `tx.Rollback()` are called **synchronously** — should be `await tx.CommitAsync()` and `await tx.RollbackAsync()`. Synchronous commit blocks the thread pool thread during I/O. |
| **MEDIUM** | The `catch` block calls `tx.Rollback(); throw;` — if `Rollback` itself throws (e.g., connection dropped), the original exception is swallowed. Use `await tx.RollbackAsync()` in a separate try-catch. |
| **LOW** | `GetUser` returns `null` for any user with no roles. An admin who accidentally removes all roles from a user will see an opaque `"Invalid credentials"` on login with no explanation. |

---

## FILE 9: `Services/MqttService.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `HandleDisconnect` uses an infinite `while(true)` loop with a **fixed 5-second retry**, no exponential backoff, and passes `CancellationToken.None` to `Connect`. When the app shuts down, `HandleDisconnect` will keep attempting reconnects indefinitely and block clean shutdown. |
| **HIGH** | Both `HandleDisconnect` and `MqttBackgroundService.ExecuteAsync` race to reconnect on disconnect — two concurrent reconnect loops can start simultaneously. |
| **MEDIUM** | QoS `AtMostOnce` (QoS 0) — messages are fire-and-forget. If the broker drops a message during a network hiccup, sensor data is silently lost. QoS 1 (at least once) is safer for cumulative counters. |
| **MEDIUM** | `Debug.WriteLine` inside `Connect` — inappropriate for production; use `_logger`. |
| **LOW** | `Publish` calls `Connect(CancellationToken.None)` — no cancellation support during shutdown. |

---

## FILE 10: `Services/MqttBackgroundService.cs`

| Severity | Issue |
|----------|-------|
| **MEDIUM** | Double reconnect logic: `HandleDisconnect` in `MqttService` retries every 5s AND `ExecuteAsync` here retries every 5s if `!IsConnected`. Both can fire simultaneously and race. Only one should be responsible for reconnection. |
| **MEDIUM** | On error, the service delays **30 seconds** before retry. Exponential backoff with a cap (1s → 2s → 4s → ... → 60s) is better than a fixed 30s. |

---

## FILE 11: `Services/MqttMessageProcessor.cs`

| Severity | Issue |
|----------|-------|
| **HIGH** | `Task.Run(() => ProcessMessagesAsync(_cts.Token))` started in constructor — if the processor task faults and the `ContinueWith` fires, the processor is **permanently dead**. Messages queue up to 5000 then drop. There is no restart mechanism. |
| **HIGH** | `Task.Run(async () => { var summaries = await repo.GetBuildingSummary(); ... })` inside `SendSignalRUpdates` — wrapping an async method in `Task.Run` wastes a thread pool thread on a yield. Call it directly. |
| **MEDIUM** | `_lastKnownCounts` is a plain `Dictionary<string, (int In, int Out)>` — safe only because `SingleReader = true`. Should be `ConcurrentDictionary` or at minimum documented with the threading assumption. |
| **MEDIUM** | `payloadSequence.ToArray()` then `Encoding.UTF8.GetString(payloadBytes)` — two allocations per message. Use `Encoding.UTF8.GetString(payloadSequence)` directly (ReadOnlySequence overload available in .NET 8). |
| **MEDIUM** | The 200ms batch window uses `Task.Delay(10)` in a spin loop polling the channel (~20 poll iterations per window). Use `WaitToReadAsync` with a linked CancellationToken timeout instead. |

---

## FILE 12: `Services/SensorHealthBackgroundService.cs`

| Severity | Issue |
|----------|-------|
| **LOW** | No exponential backoff — if health checks consistently fail (DB down), they retry every 2 minutes forever. Consider progressive backoff. |
| **LOW** | Good structure otherwise — proper scope creation, try-catch, CancellationToken handling. |

---

## FILE 13: `Services/SensorHealthService.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `Task.WhenAll(sensors.Select(CheckSensorAsync))` fires **all sensor health checks concurrently**. Each `CheckSensorAsync` opens at least 2 DB connections. With 20 sensors that is 40 simultaneous DB connections every 2 minutes — consuming 40% of the default connection pool. Add a `SemaphoreSlim` to cap concurrency to 5. |
| **HIGH** | `_sensorCache.InitializeAsync()` is called inside `CheckAsync` on every health check cycle. If not yet initialized it loads from DB; once initialized it's a cheap check. The overhead is low but calling it in a loop is unnecessary — call once at startup. |
| **MEDIUM** | Single ping attempt marks sensor as `Idle` — a single dropped ICMP packet causes a false downgrade. Consider 2-of-3 ping attempts. |

---

## FILE 14: `Services/DataRetentionBackgroundService.cs`

| Severity | Issue |
|----------|-------|
| **MEDIUM** | `Task.Delay(TimeSpan.FromHours(24))` — the job runs 24 hours after startup, not at a fixed time of day. Over time it drifts to run during peak hours. Calculate delay to the next 2 AM instead. |
| **LOW** | Shutdown handling is correct — `OperationCanceledException` from `Task.Delay` breaks the loop cleanly. |

---

## FILE 15: `Services/DataRetentionService.cs`

| Severity | Issue |
|----------|-------|
| **CRITICAL** | `id NOT IN (SELECT id FROM people_counter_log_archive)` — the most dangerous query in the codebase. As the archive table grows to millions of rows, SQL Server must materialize the entire archive ID set for every batch. At 10M rows this query can run for minutes, holding locks and blocking MQTT inserts. Replace with `NOT EXISTS` or `LEFT JOIN ... WHERE archive.id IS NULL`. |
| **HIGH** | A new `SqlConnection` is opened for **every batch iteration** inside the `while(true)` loop. Open one connection, loop batches, close. |
| **HIGH** | `await Task.Delay(500)` between batches passes no `CancellationToken` — if shutdown occurs during inter-batch sleep, the service hangs 500ms per batch. Pass the stopping token. |
| **MEDIUM** | `monthStart` calculated from `DateTime.UtcNow` but `event_time` may be stored in local time depending on sensor configuration. Timezone inconsistency causes over/under-archiving. |
| **MEDIUM** | The anonymous `catch` block rolls back and re-throws but adds no log at this level — the SQL error detail is lost; only the generic message reaches the background service logger. |

---

## FILE 16: `Services/SensorCacheService.cs`

| Severity | Issue |
|----------|-------|
| **MEDIUM** | `GetAll()` returns `_cache.Values.ToList()` — allocates a new `List<Sensor>` on every call. Called on every HTTP request to `GET /device/status`. Expose `IEnumerable<Sensor>` instead. |
| **MEDIUM** | No cache invalidation for **deleted or modified sensors**. If a sensor is removed from the `Sensors` table, it remains in memory forever. The cache only grows, never shrinks. |
| **LOW** | `UpdateStatus` mutates the `Sensor` object in-place (it's a reference type). Any caller holding a reference sees mutations immediately — this is intentional but undocumented. |

---

## FILE 17: `Services/PeopleCounterHub.cs`

| Severity | Issue |
|----------|-------|
| **HIGH** | No `[Authorize]` attribute on the Hub. Any anonymous client can connect to SignalR, join any building group, and receive real-time occupancy data. Add `[Authorize]` at the class level. |
| **MEDIUM** | No validation on `building` parameter in `JoinBuilding(string building)` — a client can join group `"building:dashboard"` and receive `BuildingSummaryUpdated` messages. Validate `building` against known locations. |

---

## MODEL FILES

| File | Severity | Issue |
|------|----------|-------|
| `MqttData.cs` | MEDIUM | All properties are non-nullable with no defaults — a malformed payload missing any field produces null references in `MqttMessageProcessor`. Add null guards. |
| `MqttData.cs` | MEDIUM | `MqttPayload.Data` has no null guard — if payload has a `Device` key but `Data` is null/empty, the foreach in the processor throws NullReferenceException. |
| `PeopleCounter.cs` | LOW | `DeviceId`, `Location`, `SubLocation` are nullable reference types without `?` annotation — nullable warnings may be suppressed silently. |
| `SensorChartPointDto.cs` | LOW | `SensorChartPointDto` class appears **unused** — `GetSensorTrendAsync` returns `SensorTrendPointDto`, not `SensorChartPointDto`. Dead code. |
| `LoginRequest.cs` | MEDIUM | No `[Required]` / `[MaxLength]` attributes — allows empty strings through to DB query. |

---

## CROSS-CUTTING ISSUES

### Performance

| Severity | Issue |
|----------|-------|
| **HIGH** | No `ConfigureAwait(false)` in background service tasks. ASP.NET Core 8 has no `SynchronizationContext` so deadlocks can't happen, but it's a best-practice for library-compatible async code. |
| **HIGH** | No Dapper — all SQL uses raw `SqlCommand`. Dapper would eliminate the `SqlCommand` disposal issue, handle null mapping automatically, and reduce boilerplate by ~60%. |
| **MEDIUM** | `GetBuildingSummary` complex CTE is called on every device reset AND every MQTT batch (debounced to 3s). With 10 devices at 1 msg/sec and 3s debounce, this runs ~20 times/minute. Needs a covering index on `people_counter_log (device_id, created_at, in_count, out_count)`. |

### Security

| Severity | Issue |
|----------|-------|
| **CRITICAL** | All secrets in `appsettings.json` which is committed to git — anyone with repo access has DB + MQTT credentials. |
| **HIGH** | No HTTPS — all traffic including auth cookies and sensor data is unencrypted on the LAN. |

---

## COMPLETE RECOMMENDED INDEX SCRIPT

```sql
-- ============================================================
-- PeopleCounter_Backend - Recommended Index Creation Script
-- Run during off-peak hours
-- ============================================================

-- people_counter_log
-- Supports: GetBuildingSummary, GetSensorsByBuilding,
--           GetLatestLogicalDeviceByIdsAysnc, DataRetention
CREATE INDEX IX_pcl_device_created
    ON dbo.people_counter_log (device_id, created_at DESC)
    INCLUDE (in_count, out_count, location, sublocation, event_time, capacity);

-- Supports: GetBuildingByDevice, trend queries
CREATE INDEX IX_pcl_device_event
    ON dbo.people_counter_log (device_id, event_time DESC)
    INCLUDE (in_count, out_count, location, sublocation);

-- Supports: GetSensorsByBuilding WHERE location = @building
CREATE INDEX IX_pcl_location_created
    ON dbo.people_counter_log (location, created_at DESC)
    INCLUDE (device_id, in_count, out_count, sublocation, event_time, capacity);

-- Supports: DataRetention WHERE event_time < @monthStart
-- Critical for batch archive/delete performance
CREATE INDEX IX_pcl_event_time
    ON dbo.people_counter_log (event_time ASC)
    INCLUDE (id, device_id, location, sublocation, in_count, out_count, capacity, created_at);

-- people_counter_log_archive (mirror of live table)
CREATE INDEX IX_pcla_device_event
    ON dbo.people_counter_log_archive (device_id, event_time DESC)
    INCLUDE (in_count, out_count, location, sublocation);

-- Critical: fixes the NOT IN subquery in DataRetentionService
-- Without this, every batch does a full scan of the archive
CREATE UNIQUE INDEX IX_pcla_id
    ON dbo.people_counter_log_archive (id);

-- people_counter_resets
-- Used in every CTE with ROW_NUMBER() OVER (PARTITION BY device_id ORDER BY reset_time DESC)
CREATE INDEX IX_pcr_device_reset_time
    ON dbo.people_counter_resets (device_id, reset_time DESC)
    INCLUDE (reset_in_count, reset_out_count);

-- Sensors
CREATE UNIQUE INDEX IX_sensors_device
    ON dbo.Sensors (Device)
    INCLUDE (Id, Location, IpAddress, IsOnline, LastSeen, Status);

-- Users
CREATE UNIQUE INDEX IX_users_username
    ON dbo.Users (Username)
    WHERE IsActive = 1;

-- UserRoles
CREATE INDEX IX_userroles_userid
    ON dbo.UserRoles (UserId);
```

---

## `appsettings.Production.json` TEMPLATE

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Warning",
      "Microsoft.AspNetCore": "Warning",
      "PeopleCounter_Backend": "Information"
    }
  },
  "EventLog": {
    "LogName": "Application",
    "SourceName": "PeopleCounterBackend",
    "LogLevel": {
      "Default": "Warning",
      "PeopleCounter_Backend": "Information"
    }
  },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:8888",
  "ConnectionStrings": {
    "DefaultConnection": "Server=.\\SQLEXPRESS;Database=people_counter_db;User Id=peoplecounter_user;Password=%DB_PASSWORD%;TrustServerCertificate=True;Min Pool Size=5;Max Pool Size=100;Connection Timeout=30;Command Timeout=60;Encrypt=False;"
  },
  "Mqtt": {
    "Host": "%MQTT_HOST%",
    "Port": 8883,
    "Username": "%MQTT_USERNAME%",
    "Password": "%MQTT_PASSWORD%",
    "Topic": "testtopic",
    "ClientIdPrefix": "peoplecounter",
    "UseTls": true
  },
  "CorsAllowedOrigins": [
    "http://192.168.88.10",
    "http://dashboard.local"
  ]
}
```

> Replace `%DB_PASSWORD%`, `%MQTT_PASSWORD%` etc. with Windows environment variables set on the service machine, or use the Windows Credential Manager. Never commit real values.

---

## PRODUCTION DEPLOYMENT CHECKLIST

### Database
- [ ] Migrate from LocalDB to SQL Server Express / Standard with named instance
- [ ] Run the full index creation script above
- [ ] Create a dedicated SQL login with only `SELECT`, `INSERT`, `UPDATE`, `DELETE` on the specific tables (no `db_owner`)
- [ ] Enable SQL Server Automatic Tuning (Query Store)
- [ ] Set `MAXDOP = 2` and `Cost Threshold for Parallelism = 50` on the SQL instance
- [ ] Configure max server memory to leave 20% free for OS
- [ ] Set up automated daily full backup + hourly transaction log backup

### Application
- [ ] Replace hardcoded credentials with environment variables or Windows Credential Manager
- [ ] Fix role case consistency: standardize `"Admin"` everywhere (fix `"admin"` in `MqttController`)
- [ ] Add `[Authorize]` to all data endpoints in `DeviceController` and `MqttController`
- [ ] Add `[Authorize]` to `PeopleCounterHub`
- [ ] Fix `CookieSecurePolicy.None` → `Always` and configure HTTPS or TLS offload
- [ ] Replace `SetIsOriginAllowed(_ => true)` with an explicit origin whitelist from config
- [ ] Fix `UseWebSockets()` ordering in `Program.cs` — move above `UseAuthentication()`
- [ ] Fix `SensorRepository` — wrap all `SqlCommand` in `using` statements
- [ ] Fix `GetByDeviceAsync` — use `ReadAsync()` not `Read()`
- [ ] Fix `HandleDisconnect` in `MqttService` — add exponential backoff + CancellationToken
- [ ] Fix `DataRetentionService` — replace `NOT IN` subquery with `NOT EXISTS`
- [ ] Fix `UserRepository` — use `CommitAsync()` / `RollbackAsync()`
- [ ] Fix `SensorHealthService` — add `SemaphoreSlim` concurrency cap (max 5 concurrent)
- [ ] Pass `stoppingToken` to `Task.Delay(500)` in `DataRetentionService` batch loop
- [ ] Unify login error message to prevent username enumeration
- [ ] Add rate limiting to `/auth/login` (e.g., 10 attempts / 5 min per IP)
- [ ] Restrict `/health` endpoint to local IPs or require authorization
- [ ] Add `[MaxLength]` and `[Required]` to `LoginRequest`, `CreateUserRequest`
- [ ] Add null guards to `MqttPayload.Data` in the message processor

### Infrastructure
- [ ] Configure Windows Service to run as a dedicated low-privilege service account
- [ ] Set up Windows Event Log collection or forward logs to a log aggregator
- [ ] Configure firewall to restrict port 8888 to LAN only
- [ ] Enable Windows Firewall outbound rule to allow MQTT port 8883 to the broker
- [ ] Create a SQL Agent job or Windows Task Scheduler task to run archive job at 2 AM daily
- [ ] Remove `appsettings.json` secrets from git history (`git filter-branch` or BFG Repo Cleaner)

---

## LOAD TESTING RECOMMENDATION

Use **k6** (free, scriptable) against these scenarios:

1. **MQTT burst test** — Simulate 20 devices sending 1 message/second for 10 minutes. Monitor queue depth, DB insert latency, SignalR broadcast lag.
2. **Building summary read** — 50 concurrent clients hitting `GET /mqtt/buildings` every second for 5 minutes. Monitor query time and connection pool exhaustion.
3. **Health check flood** — Trigger sensor health check manually while 50 clients are hitting the API. Watch for pool starvation.
4. **Data retention under load** — Trigger the archive job while MQTT data is flowing. Confirm no lock contention drops MQTT inserts.
5. **Auth brute force** — 500 req/s against `POST /auth/login` — verify rate limiting kicks in before CPU spikes.

---

## PRIORITY ORDER FOR FIXES

| Priority | Severity | Fix |
|----------|----------|-----|
| 1 | CRITICAL | Move secrets out of `appsettings.json` |
| 2 | CRITICAL | Migrate from LocalDB to SQL Server Express |
| 3 | CRITICAL | Fix `DataRetentionService` `NOT IN` query → `NOT EXISTS` |
| 4 | CRITICAL | Add `[Authorize]` to all unprotected endpoints and SignalR Hub |
| 5 | CRITICAL | Fix role case inconsistency (`"admin"` vs `"Admin"`) |
| 6 | HIGH | Fix `SensorRepository` — wrap commands in `using`, fix sync `Read()` |
| 7 | HIGH | Fix MQTT `HandleDisconnect` — exponential backoff + CancellationToken |
| 8 | HIGH | Fix `SensorHealthService` — cap concurrent DB connections with `SemaphoreSlim` |
| 9 | HIGH | Add connection pool settings to connection string |
| 10 | HIGH | Run the full index creation script |
| 11 | HIGH | Fix CORS whitelist + Cookie secure policy |
| 12 | HIGH | Add rate limiting to login |
| 13 | HIGH | Fix `ResetAllDevicesInBuilding` N+1 pattern |
| 14 | HIGH | Fix `UseWebSockets` ordering in `Program.cs` |
| 15 | HIGH | Fix `tx.Commit`/`Rollback` to async versions in `UserRepository` |
| 16 | MEDIUM | Unify login error message (username enumeration) |
| 17 | MEDIUM | Add null guards to `MqttPayload` model |
| 18 | MEDIUM | Add `SemaphoreSlim` restart mechanism to `MqttMessageProcessor` |
| 19 | MEDIUM | Fix `SensorCacheService.GetAll()` allocation |
| 20 | LOW | Remove dead code `SensorChartPointDto` |
