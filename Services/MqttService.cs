using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Models;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace PeopleCounter_Backend.Services
{
    public class MqttService
    {
        private readonly IMqttClient _client;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MqttService> _logger;
        private readonly MqttOptions _mqttOptions;
        private readonly IHubContext<PeopleCounterHub> _hubContext;
        private readonly IMemoryCache _cache;

        public bool IsConnected => _client.IsConnected;

        public MqttService(IServiceScopeFactory scopeFactory, ILogger<MqttService> logger,
            IOptions<MqttOptions> mqttOptions, IHubContext<PeopleCounterHub> hub)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _mqttOptions = mqttOptions.Value;
            _hubContext = hub;
            var factory = new MqttClientFactory();
            _client = factory.CreateMqttClient();

            _client.ApplicationMessageReceivedAsync += HandleMessage;
            _client.DisconnectedAsync += HandleDisconnect;
        }

        private async Task HandleDisconnect(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT disconnected. Reconnecting in 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            await Connect(CancellationToken.None);
        }

        public MqttService(IServiceScopeFactory scopeFactory,ILogger<MqttService> logger,IOptions<MqttOptions> mqttOptions,IHubContext<PeopleCounterHub> hub,IMemoryCache cache)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _mqttOptions = mqttOptions.Value;
            _hubContext = hub;
            _cache = cache;

            var factory = new MqttClientFactory();
            _client = factory.CreateMqttClient();

            _client.ApplicationMessageReceivedAsync += HandleMessage;
            _client.DisconnectedAsync += HandleDisconnect;
        }

        private async Task HandleMessage(MqttApplicationMessageReceivedEventArgs e)
        {
            try
            {
                var payloadSequence = e.ApplicationMessage.Payload;
                if (payloadSequence.IsEmpty)
                    return;

                byte[] payloadBytes = payloadSequence.ToArray();
                var payload = Encoding.UTF8.GetString(payloadBytes);
                _logger.LogInformation("MQTT Raw Payload: {Payload}", payload);

                var messages = JsonSerializer.Deserialize<List<MqttPayload>>(payload);
                if (messages is null || messages.Count == 0)
                    return;

                using var scope = _scopeFactory.CreateScope();

                var peopleRepo = scope.ServiceProvider
                    .GetRequiredService<PeopleCounterRepository>();

                var sensorRepo = scope.ServiceProvider
                    .GetRequiredService<SensorRepository>();

                foreach (var device in messages)
                {
                    var location = device.Data.FirstOrDefault()?.Location ?? "";
                    var cacheKey = $"sensor:{device.Device}";

                    if (!_cache.TryGetValue(cacheKey, out Sensor sensor))
                    {
                        sensor = await sensorRepo.GetByDeviceAsync(device.Device)
                                 ?? new Sensor
                                 {
                                     Device = device.Device,
                                     Location = location
                                 };
                    }

                    sensor.IsOnline = true;
                    sensor.LastSeen = DateTime.UtcNow;
                    sensor.Location = location;

                    _cache.Set(
                        cacheKey,
                        sensor,
                        new MemoryCacheEntryOptions
                        {
                            SlidingExpiration = TimeSpan.FromMinutes(5)
                        });

                    if (sensor.Id == 0)
                    {
                        await sensorRepo.InsertIfNotExistsAsync(
                            sensor.Device,
                            sensor.Location,
                            sensor.IpAddress
                        );

                        var dbSensor = await sensorRepo.GetByDeviceAsync(sensor.Device);
                        if (dbSensor != null)
                            sensor.Id = dbSensor.Id;
                    }

                    await sensorRepo.UpdateStatusAsync(sensor.Id, true);
                }

                var records = new List<PeopleCounter>();

                foreach (var device in messages)
                {
                    foreach (var d in device.Data)
                    {
                        if (!DateTime.TryParse(d.TimeStamp, out var ts)) continue;

                        records.Add(new PeopleCounter
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

                if (records.Count == 0) return;

                await peopleRepo.InsertAsync(records);

                var devices = await peopleRepo.GetLatestLogicalDevicesAsync();

                foreach (var d in devices)
                {
                    await _hubContext.Clients
                        .Group($"building:{d.Location}")
                        .SendAsync("SensorUpdated", new
                        {
                            deviceId = d.DeviceId,
                            location = d.Location,
                            sublocation = d.SubLocation,
                            inCount = d.InCount,
                            outCount = d.OutCount,
                            capacity = d.Capacity,
                            eventTime = d.EventTime
                        });
                }

                var summaries = await peopleRepo.GetBuildingSummaryAsync();

                await _hubContext.Clients
                    .Group("dashboard")
                    .SendAsync("BuildingSummaryUpdated", summaries);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MQTT message processing failed");
                Debug.WriteLine($"MQTT PROCESS ERROR: {ex}");
            }
        }


        public async Task Connect(CancellationToken ct)
        {
            if (_client.IsConnected) return;

            var options = new MqttClientOptionsBuilder()
                .WithClientId($"{_mqttOptions.ClientIdPrefix}{Guid.NewGuid()}")
                .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                .WithCredentials(_mqttOptions.Username, _mqttOptions.Password)
                .WithTlsOptions(tls => tls.UseTls())
                .Build();

            await _client.ConnectAsync(options, ct);
            await _client.SubscribeAsync(_mqttOptions.Topic, MqttQualityOfServiceLevel.AtMostOnce);

            Debug.WriteLine("MQTT CONNECTED");
            _logger.LogInformation("MQTT connected and subscribed");
        }

        public async Task StopAsync()
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();
        }

        public async Task Subscribe(string topic)
        {
            if (!_client.IsConnected)
            {
                Debug.WriteLine("MQTT not connected yet. Skipping subscribe.");
                return;
            }

            await _client.SubscribeAsync(
                topic,
                MqttQualityOfServiceLevel.AtMostOnce
            );
        }


        public async Task Publish(string topic, string payload)
        {
            await Connect(CancellationToken.None);

            var message = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtMostOnce)
                .Build();

            await _client.PublishAsync(message);
        }
    }
}
