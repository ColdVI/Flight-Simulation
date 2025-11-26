using FlightRadarAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlightRadarAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class SimulationController : ControllerBase
    {
        private readonly SimulationService _simulationService;

        public SimulationController(SimulationService simulationService)
        {
            _simulationService = simulationService;
        }

        [HttpPost("start")]
        public IActionResult Start()
        {
            _simulationService.Start();
            return Ok(new { message = "Simulation started" });
        }

        [HttpPost("stop")]
        public IActionResult Stop()
        {
            _simulationService.Stop();
            return Ok(new { message = "Simulation stopped" });
        }

        [HttpPost("reset")]
        public async Task<IActionResult> Reset(CancellationToken cancellationToken)
        {
            await _simulationService.ResetAsync(cancellationToken);
            return Ok(new { message = "Simulation reset" });
        }

        [HttpPost("speed")]
        public IActionResult SetSpeed([FromBody] SpeedRequest request)
        {
            _simulationService.SetSpeed(request.Multiplier);
            return Ok(new { message = $"Speed set to {request.Multiplier}x" });
        }
    }

    public class SpeedRequest
    {
        public double Multiplier { get; set; }
    }
}
