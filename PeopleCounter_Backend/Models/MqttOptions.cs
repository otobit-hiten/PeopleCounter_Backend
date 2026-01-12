namespace PeopleCounter_Backend.Models
{
    public class MqttOptions
    {
        public string Host { get; set; } = default!;
        public int Port { get; set; }
        public string Username { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string Topic { get; set; } = default!;
        public string ClientIdPrefix { get; set; } = default!;
    }
}
