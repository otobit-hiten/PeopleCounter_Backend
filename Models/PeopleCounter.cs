namespace PeopleCounter_Backend.Models
{
    public class PeopleCounter
    {
        public long Id { get; set; }
        public string DeviceId { get; set; }
        public string Location { get; set; }
        public string SubLocation { get; set; }
        public int InCount { get; set; }
        public int OutCount { get; set; }
        public int Capacity { get; set; }
        public string IpAddress { get; set; } = "";
        public DateTime EventTime { get; set; }
    }
}
