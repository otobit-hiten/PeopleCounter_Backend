using PeopleCounter_Backend.Models;

namespace PeopleCounter_Backend.Data
{
    public interface IUserRepository
    {
        Task<UserAuth> GetUser(string username);

        Task CreateUser(string username, string passwordHash, List<string> roles);
    }
}
