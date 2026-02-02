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
            _logger.LogWarning("⚠️ MQTT disconnected. Reconnecting in 5 seconds...");
            await Task.Delay(TimeSpan.FromSeconds(5));
            await Connect(CancellationToken.None);
        }

       
        private Task HandleMessage(MqttApplicationMessageReceivedEventArgs e)
        {
           
            _messageProcessor.EnqueueMessage(e);

            return Task.CompletedTask;
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

            _logger.LogInformation("Connecting to MQTT {Host}:{Port}...", _mqttOptions.Host, _mqttOptions.Port);
            await _client.ConnectAsync(options, ct);

            _logger.LogInformation("Subscribing to topic: {Topic}", _mqttOptions.Topic);
            await _client.SubscribeAsync(_mqttOptions.Topic, MqttQualityOfServiceLevel.AtMostOnce);

            Debug.WriteLine("MQTT CONNECTED");
            _logger.LogInformation("✅ MQTT connected and subscribed");
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