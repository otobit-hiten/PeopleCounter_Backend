namespace PeopleCounter_Backend.Models
{
    public record BuildingSummary(
        string Building,
        int TotalIn,
        int TotalOut,
        int TotalCapacity);
}
