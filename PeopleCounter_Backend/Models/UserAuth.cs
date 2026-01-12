using System.Globalization;

namespace PeopleCounter_Backend.Models
{
    public class UserAuth
    {
        public int UserId { get; set; }
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public List<string> Roles { get; set; } = new();

    }
}
