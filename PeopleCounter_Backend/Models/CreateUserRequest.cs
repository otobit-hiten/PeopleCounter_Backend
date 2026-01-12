namespace PeopleCounter_Backend.Models
{
    public class CreateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        public List<string> Roles { get; set; } = new();
    }
}
