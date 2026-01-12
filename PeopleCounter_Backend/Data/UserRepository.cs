using Microsoft.Data.SqlClient;
using PeopleCounter_Backend.Models;

namespace PeopleCounter_Backend.Data
{
    public class UserRepository : IUserRepository
    {
        private readonly string _connectionString;

        public UserRepository(IConfiguration config) {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

            public async Task CreateUser(string username, string passwordHash, List<string> roles)
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var tx = conn.BeginTransaction();

                try
                {
                    var insertUser = @"
                        INSERT INTO Users(Username, PasswordHash, IsActive)
                        OUTPUT INSERTED.UserId
                        VALUES (@username, @passwordHash, 1)";

                    using var userCmd = new SqlCommand(insertUser, conn,tx);

                    userCmd.Parameters.AddWithValue("@username", username);
                    userCmd.Parameters.AddWithValue("@passwordHash", passwordHash);

                    var userId = (int)await userCmd.ExecuteScalarAsync();

                    var roleSql = @"INSERT INTO UserRoles(UserId,RoleId)
                                    SELECT @userId, RoleId FROM Roles WHERE RoleName = @roleName";

                    foreach (var role in roles)
                    {
                        using var roleCmd = new SqlCommand(roleSql, conn, tx);
                        roleCmd.Parameters.AddWithValue("@userId", userId);
                        roleCmd.Parameters.AddWithValue("@roleName", role);

                        var rows = await roleCmd.ExecuteNonQueryAsync();
                        if (rows == 0)
                            throw new Exception($"Role '{role}' does not exist");
                    }

                    tx.Commit();
                }
                catch (Exception ex) {
                    tx.Rollback();
                    throw;
                }
            }

        async Task<UserAuth> IUserRepository.GetUser(string username)
        {
            const string sql = @"SELECT 
                u.UserId,
                u.Username,
                u.PasswordHash,
                r.RoleName
            FROM Users u
            JOIN UserRoles ur ON u.UserId = ur.UserId
            JOIN Roles r ON ur.RoleId = r.RoleId
            WHERE u.Username = @username
              AND u.IsActive = 1";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql,conn);

            cmd.Parameters.AddWithValue("@Username", username);

            await conn.OpenAsync(); 

            using var reader = await cmd.ExecuteReaderAsync();

            UserAuth user = null;

            while (await reader.ReadAsync()) { 
                
                if(user == null)
                {
                    user = new UserAuth
                    {
                        UserId = reader.GetInt32(0),
                        Username = reader.GetString(1),
                        PasswordHash = reader.GetString(2)
                    };
                }

                user.Roles.Add(reader.GetString(3));
            }
            return user;
        }
    }
}
