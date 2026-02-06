using Microsoft.AspNetCore.SignalR;
using MQTTnet;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Models;
using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace PeopleCounter_Backend.Services
{
    public class MqttMessageProcessor
    {
        private readonly Channel<MqttApplicationMessageReceivedEventArgs> _messageQueue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHubContext<PeopleCounterHub> _hubContext;
        private readonly ILogger<MqttMessageProcessor> _logger;
        private readonly CancellationTokenSource _cts = new();

        private const int BATCH_INTERVAL_MS = 200;      
        private const int MAX_BATCH_SIZE = 100;        
        private const int QUEUE_WARNING_THRESHOLD = 500;
        private int _queueDepth = 0;

        public MqttMessageProcessor(
            IServiceScopeFactory scopeFactory,
            IHubContext<PeopleCounterHub> hubContext,
            ILogger<MqttMessageProcessor> logger)
        {
            _scopeFactory = scopeFactory;
            _hubContext = hubContext;
            _logger = logger;

            _messageQueue = Channel.CreateUnbounded<MqttApplicationMessageReceivedEventArgs>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,   
                    SingleWriter = false 
                });

            _logger.LogInformation("MqttMessageProcessor starting background processor");
            Task.Run(() => ProcessMessagesAsync(_cts.Token));
        }

        public void EnqueueMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            if (_messageQueue.Writer.TryWrite(e))
            {
                var depth = Interlocked.Increment(ref _queueDepth);

                if (depth > QUEUE_WARNING_THRESHOLD)
                {
                    _logger.LogWarning(
                        "High queue depth: ~{Count} messages. Processing may be falling behind.",
                        depth);
                }
            }
        }

        public void Stop()
        {
            _logger.LogInformation("Stopping MqttMessageProcessor...");
            _cts.Cancel();
            _messageQueue.Writer.Complete();
        }

        private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Message processor background task started");

            var buffer = new List<MqttApplicationMessageReceivedEventArgs>();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    buffer.Clear();

                    if (await _messageQueue.Reader.WaitToReadAsync(cancellationToken))
                    {
                        var deadline = DateTime.UtcNow.AddMilliseconds(BATCH_INTERVAL_MS);

                        while (buffer.Count < MAX_BATCH_SIZE && DateTime.UtcNow < deadline)
                        {
                            if (_messageQueue.Reader.TryRead(out var msg))
                            {
                                buffer.Add(msg);
                            }
                            else
                            {
                                await Task.Delay(10, cancellationToken);
                            }
                        }

                        while (buffer.Count < MAX_BATCH_SIZE && _messageQueue.Reader.TryRead(out var msg))
                        {
                            buffer.Add(msg);
                        }

                        if (buffer.Count > 0)
                        {
                            Interlocked.Add(ref _queueDepth, -buffer.Count);
                            _logger.LogInformation("Processing batch of {Count} MQTT messages", buffer.Count);
                            await ProcessMessageBatch(buffer);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Message processor cancelled - shutting down gracefully");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in message processor main loop");
                    await Task.Delay(1000, cancellationToken); 
                }
            }

            _logger.LogInformation("Message processor background task stopped");
        }

        private async Task ProcessMessageBatch(List<MqttApplicationMessageReceivedEventArgs> messages)
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var allRecords = new List<PeopleCounter>();

                foreach (var e in messages)
                {
                    try
                    {
                        var payloadSequence = e.ApplicationMessage.Payload;
                        if (payloadSequence.IsEmpty) continue;

                        byte[] payloadBytes = payloadSequence.ToArray();
                        var payload = Encoding.UTF8.GetString(payloadBytes);

                        _logger.LogDebug("MQTT Payload: {Payload}", payload);

                        var mqttMessages = JsonSerializer.Deserialize<List<MqttPayload>>(payload);
                        if (mqttMessages is null || mqttMessages.Count == 0) continue;

                        foreach (var device in mqttMessages)
                        {
                            foreach (var d in device.Data)
                            {
                                if (!DateTime.TryParse(d.TimeStamp, out var ts))
                                {
                                    _logger.LogWarning(
                                        "Invalid timestamp '{TimeStamp}' for device {DeviceId}",
                                        d.TimeStamp,
                                        device.Device);
                                    continue;
                                }

                                allRecords.Add(new PeopleCounter
                                {
                                    DeviceId = device.Device,
                                    Location = d.Location,
                                    SubLocation = d.SubLocation,
                                    InCount = d.Total_IN,
                                    OutCount = d.Total_Out,
                                    Capacity = d.Capacity,
                                    EventTime = ts
                                });
                            }
                        }
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogError(ex, "Failed to deserialize MQTT message");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to parse individual MQTT message");
                    }
                }

                if (allRecords.Count == 0)
                {
                    _logger.LogWarning("No valid records from {Count} MQTT messages", messages.Count);
                    return;
                }

                _logger.LogInformation(
                    "Parsed {RecordCount} sensor records from {MessageCount} MQTT messages",
                    allRecords.Count,
                    messages.Count);

              
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<PeopleCounterRepository>();

               
                var insertStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await repo.InsertDataAsync(allRecords);
                insertStopwatch.Stop();

                _logger.LogInformation(
                    "Bulk insert: {Ms}ms for {Count} records",
                    insertStopwatch.ElapsedMilliseconds,
                    allRecords.Count);

               
                var deviceIds = allRecords.Select(r => r.DeviceId).Distinct().ToList();

                var queryStopwatch = System.Diagnostics.Stopwatch.StartNew();
                var devices = await repo.GetLatestLogicalDeviceByIdsAysnc(deviceIds);
                queryStopwatch.Stop();

                _logger.LogInformation(
                    "Batch query: {Ms}ms for {Count} devices",
                    queryStopwatch.ElapsedMilliseconds,
                    deviceIds.Count);

                if (devices == null || devices.Count == 0)
                {
                    _logger.LogWarning("No devices returned from batch query");
                    return;
                }

                var signalRStopwatch = System.Diagnostics.Stopwatch.StartNew();
                await SendSignalRUpdates(devices, repo);
                signalRStopwatch.Stop();

                _logger.LogInformation("SignalR updates: {Ms}ms", signalRStopwatch.ElapsedMilliseconds);

               

                totalStopwatch.Stop();

                _logger.LogInformation(
                    "Batch summary: {MessageCount} msgs → {RecordCount} records → " +
                    "{DeviceCount} devices | Insert: {InsertMs}ms, Query: {QueryMs}ms, " +
                    "SignalR: {SignalRMs}ms, Total: {TotalMs}ms",
                    messages.Count,
                    allRecords.Count,
                    deviceIds.Count,
                    insertStopwatch.ElapsedMilliseconds,
                    queryStopwatch.ElapsedMilliseconds,
                    signalRStopwatch.ElapsedMilliseconds,
                    totalStopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Batch processing failed");
            }
        }

        private async Task SendSignalRUpdates(List<PeopleCounter> devices, PeopleCounterRepository repo)
        {
            var tasks = new List<Task>();

            foreach (var device in devices)
            {
                tasks.Add(_hubContext.Clients
                    .Group($"building:{device.Location}")
                    .SendAsync("SensorUpdated", device));
            }

            _logger.LogDebug("Sending {Count} sensor updates", devices.Count);

            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    var summaries = await repo.GetBuildingSummaryAsync();

                    await _hubContext.Clients
                        .Group("dashboard")
                        .SendAsync("BuildingSummaryUpdated", summaries);

                    _logger.LogDebug("Sent building summary for {Count} buildings", summaries.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to send building summary");
                }
            }));

            await Task.WhenAll(tasks);
        }
    }
}