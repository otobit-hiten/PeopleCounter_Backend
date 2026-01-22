using Microsoft.AspNetCore.Mvc;
using PeopleCounter_Backend.Data;
using PeopleCounter_Backend.Services;

namespace PeopleCounter_Backend.Controllers
{
    [ApiController]
    [Route("mqtt")]
    public class MqttController : ControllerBase
    {
        private readonly MqttService _mqttService;
        private readonly PeopleCounterRepository _repository;

        public MqttController(MqttService mqttService, PeopleCounterRepository repository)
        {
            _mqttService = mqttService;
            _repository = repository;
        }

        [HttpPost("publish")]
        public async Task<IActionResult> Publish([FromBody] PublishDto dto)
        {
            await _mqttService.Publish(dto.Topic, dto.Payload);
            return Ok("Published");
        }

        [HttpGet("getAllDevice")]
        public async Task<IActionResult> GetAllDevice()
        {
            var data = await _repository.GetLatestPerDeviceAsync();
            return Ok(data);
        }

        [HttpGet("buildings")]
        public async Task<IActionResult> GetBuildings()
        {
            var data = await _repository.GetBuildingSummaryAsync();
            return Ok(data);
        }

        [HttpGet("building/{building}")]
        public async Task<IActionResult> GetBuildingDevices(string building)
        {
            var data = await _repository.GetSensorsByBuildingAsync(building);
            return Ok(data);
        }


    }

    public record PublishDto(string Topic, string Payload);
}
