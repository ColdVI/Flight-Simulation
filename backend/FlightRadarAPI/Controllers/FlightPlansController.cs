using FlightRadarAPI.Models;
using FlightRadarAPI.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;

namespace FlightRadarAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FlightPlansController : ControllerBase
    {
        private readonly SimulationService _simulationService;
        private readonly ILogger<FlightPlansController> _logger;

        public FlightPlansController(SimulationService simulationService, ILogger<FlightPlansController> logger)
        {
            _simulationService = simulationService;
            _logger = logger;
        }

        [HttpGet("airports")]
        public async Task<ActionResult<IEnumerable<AirportSummary>>> GetAirports(CancellationToken cancellationToken)
        {
            var airports = await _simulationService.GetAirportsAsync(cancellationToken);
            return Ok(airports);
        }

        [HttpGet("aircraft")]
        public async Task<ActionResult<IEnumerable<AircraftSummary>>> GetAircraft([FromQuery] bool availableOnly, CancellationToken cancellationToken)
        {
            var aircraft = await _simulationService.GetAircraftAsync(cancellationToken);
            if (availableOnly)
            {
                aircraft = aircraft.Where(a => a.IsAvailable).ToList();
            }

            return Ok(aircraft);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<FlightPlanSummary>>> GetPlans(CancellationToken cancellationToken)
        {
            var plans = await _simulationService.GetFlightPlansAsync(cancellationToken);
            return Ok(plans);
        }

        [HttpGet("{callsign}")]
        public async Task<ActionResult<FlightPlanSummary>> GetPlan(string callsign, CancellationToken cancellationToken)
        {
            var plan = await _simulationService.GetFlightPlanAsync(callsign, cancellationToken);
            if (plan is null)
            {
                return NotFound();
            }

            return Ok(plan);
        }

        [HttpPost]
        public async Task<ActionResult<FlightPlanSummary>> CreatePlan([FromBody] FlightPlanRequest request, CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            try
            {
                var created = await _simulationService.CreateFlightPlanAsync(request, cancellationToken);
                if (created is null)
                {
                    return StatusCode(500, new { message = "Failed to create flight plan." });
                }

                return CreatedAtAction(nameof(GetPlan), new { callsign = created.Callsign }, created);
            }
            catch (InvalidOperationException ex)
            {
                return ex.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                       ex.Message.Contains("assigned to an active flight", StringComparison.OrdinalIgnoreCase)
                    ? Conflict(new { message = ex.Message })
                    : BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create flight plan.");
                return StatusCode(500, new { message = "Unexpected error while creating flight plan." });
            }
        }
    }
}
