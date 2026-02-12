namespace PeopleCounter_Backend.Models
{
    public class Sensor
    {
        public int Id { get; set; }
        public string Device { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime? LastSeen { get; set; }
    }
}
