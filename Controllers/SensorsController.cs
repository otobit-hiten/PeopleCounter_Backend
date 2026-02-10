using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using PeopleCounter_Backend.Models;

namespace PeopleCounter_Backend.Controllers
{
    [ApiController]
    [Route("api/sensors")]
    public class SensorsController : ControllerBase
    {
        private readonly IConfiguration _config;

        public SensorsController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet]
        public async Task<IActionResult> Get(string building)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            var cmd = new SqlCommand(
                @"SELECT Device, IpAddress, IsOnline, LastSeen
              FROM Sensors
              WHERE Location = @building", conn);

            cmd.Parameters.AddWithValue("@building", building);

            var list = new List<object>();

            using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                list.Add(new Sensor
                {
                    Device = reader.GetString(0),
                    IpAddress = reader.GetString(1),
                    IsOnline = reader.GetBoolean(2),
                    LastSeen = reader.IsDBNull(3) ? null : reader.GetDateTime(3)
                });
            }

            return Ok(list);
        }
    }

}
