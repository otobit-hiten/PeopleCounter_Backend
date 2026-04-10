namespace PeopleCounter_Backend.Models
{
    public class MqttData
    {
        public string Location { get; set; } = "";
        public string? SubLocation { get; set; }
        public int Total_IN { get; set; }
        public int Total_Out { get; set; }
        public int Capacity { get; set; }
        public string ipaddr { get; set; } = "";
        public string TimeStamp { get; set; } = "";
    }

    public class MqttPayload
    {
        public string Device { get; set; } = "";

        // Defaults to empty list so foreach in MqttMessageProcessor
        // is safe even if the field is missing or null in the JSON payload.
        public List<MqttData> Data { get; set; } = new();
    }
}
