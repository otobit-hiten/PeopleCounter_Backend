using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Models;
using System.Text;
using System.Text.Json;

namespace PeopleCounter_Backend.Services
{
    public class MqttService
    {
        private readonly IMqttClient _client;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MqttService> _logger;
        private readonly MqttOptions _mqttOptions;
        private readonly IHubContext<PeopleCounterHub> _hubContext;
        private readonly MqttMessageProcessor _messageProcessor;

        // Stored when Connect() is first called so HandleDisconnect can
        // respect shutdown without needing a separate parameter.
        private CancellationToken _stoppingToken = CancellationToken.None;

        public bool IsConnected => _client.IsConnected;

        public MqttService(
            IServiceScopeFactory scopeFactory,
            ILogger<MqttService> logger,
            IOptions<MqttOptions> mqttOptions,
            IHubContext<PeopleCounterHub> hub,
            MqttMessageProcessor messageProcessor)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _mqttOptions = mqttOptions.Value;
            _hubContext = hub;
            _messageProcessor = messageProcessor;

            var factory = new MqttClientFactory();
            _client = factory.CreateMqttClient();

            _client.ApplicationMessageReceivedAsync += HandleMessage;
            _client.DisconnectedAsync += HandleDisconnect;
        }

        private async Task HandleDisconnect(MqttClientDisconnectedEventArgs args)
        {
            // If shutdown is in progress, do not attempt to reconnect.
            if (_stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("MQTT disconnected during shutdown — skipping reconnect.");
                return;
            }

            _logger.LogWarning("MQTT disconnected: {Reason}", args.ReasonString);

            int retryCount = 0;

            while (!_stoppingToken.IsCancellationRequested)
            {
                retryCount++;

                // Exponential backoff: 2s, 4s, 8s, 16s, 32s, 60s (capped)
                var delaySecs = Math.Min(Math.Pow(2, retryCount), 60);
                _logger.LogInformation(
                    "Reconnect attempt {Attempt} in {Delay}s...", retryCount, delaySecs);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySecs), _stoppingToken);
                    await Connect(_stoppingToken);
                    _logger.LogInformation(
                        "MQTT reconnected successfully after {Attempt} attempt(s).", retryCount);
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("MQTT reconnect cancelled — shutting down.");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Reconnect attempt {Attempt} failed.", retryCount);
                }
            }
        }

       
        private Task HandleMessage(MqttApplicationMessageReceivedEventArgs e)
        {
           
            _messageProcessor.EnqueueMessage(e);

            return Task.CompletedTask;
        }

        public async Task Connect(CancellationToken ct)
        {
            if (_client.IsConnected) return;

            // Store so HandleDisconnect can check shutdown state
            _stoppingToken = ct;

            var builder = new MqttClientOptionsBuilder()
                .WithClientId($"{_mqttOptions.ClientIdPrefix}{Guid.NewGuid()}")
                .WithTcpServer(_mqttOptions.Host, _mqttOptions.Port)
                .WithCredentials(_mqttOptions.Username, _mqttOptions.Password);

            if (_mqttOptions.UseTls)
                builder.WithTlsOptions(tls => tls.UseTls());

            var options = builder.Build();

            _logger.LogInformation("Connecting to MQTT {Host}:{Port}...", _mqttOptions.Host, _mqttOptions.Port);
            await _client.ConnectAsync(options, ct);

            _logger.LogInformation("Subscribing to topic: {Topic}", _mqttOptions.Topic);
            await _client.SubscribeAsync(_mqttOptions.Topic, MqttQualityOfServiceLevel.AtMostOnce);

            _logger.LogInformation("MQTT connected and subscribed");
        }

        public async Task StopAsync()
        {
            if (_client.IsConnected)
                await _client.DisconnectAsync();

            _messageProcessor.Stop(); 
        }

        public async Task Subscribe(string topic)
        {
            if (!_client.IsConnected)
            {
                _logger.LogWarning("MQTT not connected yet. Skipping subscribe.");
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