using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MQTTnet;
using MQTTnet.Protocol;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Models;
using System.Diagnostics;
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

        private const int MaxReconnectAttempts = 20;
        private const int BaseReconnectDelaySeconds = 5;
        private const int MaxReconnectDelaySeconds = 300; // cap at 5 minutes

        private readonly CancellationTokenSource _reconnectCts = new();

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

        private Task HandleDisconnect(MqttClientDisconnectedEventArgs args)
        {
            _logger.LogWarning("MQTT disconnected: {Reason}", args.ReasonString);
            // Fire reconnect in background — do NOT block the MQTT event handler
            _ = Task.Run(() => ReconnectWithBackoffAsync(_reconnectCts.Token));
            return Task.CompletedTask;
        }

        private async Task ReconnectWithBackoffAsync(CancellationToken ct)
        {
            for (int attempt = 1; attempt <= MaxReconnectAttempts; attempt++)
            {
                if (ct.IsCancellationRequested) return;

                // Exponential backoff: 5s, 10s, 20s, 40s … capped at 5 minutes
                var delaySeconds = Math.Min(
                    BaseReconnectDelaySeconds * Math.Pow(2, attempt - 1),
                    MaxReconnectDelaySeconds);

                _logger.LogInformation(
                    "MQTT reconnect attempt {Attempt}/{Max} in {Delay}s...",
                    attempt, MaxReconnectAttempts, delaySeconds);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds), ct);
                    await Connect(ct);
                    _logger.LogInformation("MQTT reconnected on attempt {Attempt}.", attempt);
                    return;
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("MQTT reconnect cancelled (shutdown).");
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MQTT reconnect attempt {Attempt}/{Max} failed.", attempt, MaxReconnectAttempts);
                }
            }

            _logger.LogCritical(
                "MQTT reconnect gave up after {Max} attempts. Restart the service.", MaxReconnectAttempts);
        }

       
        private Task HandleMessage(MqttApplicationMessageReceivedEventArgs e)
        {
           
            _messageProcessor.EnqueueMessage(e);

            return Task.CompletedTask;
        }

        public async Task Connect(CancellationToken ct)
        {
            if (_client.IsConnected) return;

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

            Debug.WriteLine("MQTT CONNECTED");
            _logger.LogInformation("MQTT connected and subscribed");
        }

        public async Task StopAsync()
        {
            // Cancel any in-progress reconnect loop first
            await _reconnectCts.CancelAsync();

            if (_client.IsConnected)
                await _client.DisconnectAsync();

            _messageProcessor.Stop();
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