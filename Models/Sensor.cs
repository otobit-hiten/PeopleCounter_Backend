namespace PeopleCounter_Backend.Models
{
    public class Sensor
    {
        public int Id { get; set; }
        public string Device { get; set; } = "";
        public string Location { get; set; } = "";
        public string IpAddress { get; set; } = "";
        public bool IsOnline { get; set; }
        public DateTime? LastSeen { get; set; }
        public SensorStatus Status { get; set; } = SensorStatus.Offline;
    }

    public enum SensorStatus
    {
        Online,
        Idle,
        Offline
    }
}