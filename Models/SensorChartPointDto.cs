namespace PeopleCounter_Backend.Models
{
    public class SensorChartPointDto
    {
        public int SegmentId { get; set; }
        public DateTime Time { get; set; }
        public long TotalIn { get; set; }   
        public long TotalOut { get; set; }
    }

    public class SensorTrendPointDto
    {
        public DateTime Time { get; set; }
        public int In { get; set; }
        public int Out { get; set; }
    }
}

