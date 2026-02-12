
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Services;

namespace PeopleCounter_Backend.Controllers
{
    [ApiController]
    [Route("device")]
    public class DeviceController : ControllerBase
    {
        private readonly PeopleCounterRepository _repository;
        private readonly IHubContext<PeopleCounterHub> _hub;

        public DeviceController(PeopleCounterRepository repository, IHubContext<PeopleCounterHub> hub)
        {
            _repository = repository;
            _hub = hub;
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("{deviceId}/reset")]
        public async Task<IActionResult> ResetDevice(string deviceId)
        {
            var building = await _repository.GetBuildingByDevice(deviceId);

            await _repository.ResetDevice(deviceId);

            await _hub.Clients.Group($"building:{building}")
                .SendAsync("DeviceReset", deviceId);

            var updatedSummaries = await _repository.GetBuildingSummaryAsync();
            await _hub.Clients.Group("dashboard")
                .SendAsync("BuildingSummaryUpdated", updatedSummaries);

            return Ok(new
            {
                message = "Device reset successful",
                deviceId
            });
        }

        [Authorize(Roles = "Admin")]
        [HttpPost("building/{building}/reset")]
        public async Task<IActionResult> ResetBuilding(string building)
        {
            await _repository.ResetAllDevicesByBuildingAsync(building);

            await _hub.Clients.Group($"building:{building}")
                .SendAsync("BuildingReset", building);

            var updatedSummaries = await _repository.GetBuildingSummaryAsync();
            await _hub.Clients.Group("dashboard")
                .SendAsync("BuildingSummaryUpdated", updatedSummaries);

            return Ok(new
            {
                message = "Building reset successful",
                building
            });
        }


        //[HttpGet("chart")]
        //public async Task<IActionResult> GetSensorChart(
        //[FromQuery] string deviceId,
        //[FromQuery] DateTime from,
        //[FromQuery] DateTime to,
        //[FromQuery] string bucket = "hour")
        //{
        //    var data = await _repository.GetSensorChartAsync(deviceId, from, to, bucket);
        //    return Ok(data);
        //}


        [HttpGet("trend")]
        public async Task<IActionResult> GetSensorTrend(
       [FromQuery] string deviceId,
       [FromQuery] DateTime from,
       [FromQuery] DateTime to,
       [FromQuery] string bucket = "hour")
        {
            if (string.IsNullOrWhiteSpace(deviceId))
                return BadRequest("deviceId is required");
            DateTime adjustedFrom = from;
            DateTime adjustedTo = to;

            switch (bucket.ToLower())
            {
                case "hour":
                    break;

                case "day":
                    adjustedFrom = from.Date;
                    adjustedTo = to.Date.AddDays(1).AddSeconds(-1);
                    break;

                case "month":
                    adjustedFrom = new DateTime(from.Year, from.Month, 1);
                    adjustedTo = new DateTime(to.Year, to.Month, 1).AddMonths(1).AddSeconds(-1);
                    break;
            }

            var data = await _repository.GetSensorTrendAsync(deviceId, adjustedFrom, adjustedTo, bucket);
            return Ok(data);
        }

        [HttpGet("trendlocation")]
        public async Task<IActionResult> GetLocationTrend(
            [FromQuery] string location,
            [FromQuery] DateTime from,
            [FromQuery] DateTime to,
            [FromQuery] string bucket = "hour")

        {
            if (string.IsNullOrWhiteSpace(location))
                return BadRequest("location is required");
            DateTime adjustedFrom = from;
            DateTime adjustedTo = to;

            switch (bucket.ToLower())
            {
                case "hour":
                    break;

                case "day":
                    adjustedFrom = from.Date;
                    adjustedTo = to.Date.AddDays(1).AddTicks(-1);
                    break;

                case "month":
                    adjustedFrom = new DateTime(from.Year, from.Month, 1);
                    adjustedTo = new DateTime(to.Year, to.Month, 1).AddMonths(1).AddTicks(-1);
                    break;
            }

            var data = await _repository.GetLocationTrendAsync(location, adjustedFrom, adjustedTo, bucket);
            return Ok(data);
        }


        [HttpGet("list")]
        public async Task<IActionResult> GetDevices()
            => Ok(await _repository.GetAllDevicesAsync());


        [HttpGet("location")]
        public async Task<IActionResult> GetLocation()
            => Ok(await _repository.GetAllLocationAsync());


        [HttpGet("status")]
        public async Task<IActionResult> GetSensorStatuses()
        {
            var sensorCache = HttpContext.RequestServices.GetRequiredService<SensorCacheService>();
            await sensorCache.InitializeAsync();

            var sensors = sensorCache.GetAll()
                .Select(s => new
                {
                    s.Device,
                    s.Location,
                    s.IsOnline,
                    s.LastSeen,
                    s.IpAddress
                })
                .ToList();

            return Ok(sensors);
        }
    }
}
