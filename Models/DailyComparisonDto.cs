namespace PeopleCounter_Backend.Models
{
    public class DailyComparisonDto
    {
        public string DeviceId { get; set; } = default!;
        public string Location { get; set; } = default!;
        public string? SubLocation { get; set; }
        public DateTime Hour { get; set; }

        // Raw values — straight from sensor, no reset applied
        public int RawIn { get; set; }
        public int RawOut { get; set; }
        public int RawInside { get; set; }

        // Reset-aware values — what the client sees after reset calculation
        public int DisplayIn { get; set; }
        public int DisplayOut { get; set; }
        public int DisplayInside { get; set; }
    }
}
