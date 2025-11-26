using FlightRadarAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlightRadarAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class FlightsController : ControllerBase
    {
        private readonly SimulationService _simulationService;

        public FlightsController(SimulationService simulationService)
        {
            _simulationService = simulationService;
        }

        // GET: api/flights
        // Frontend bu adrese istek attığında güncel uçuş listesini döndürür.
        [HttpGet]
        public IActionResult GetFlights()
        {
            var flights = _simulationService.GetFlightsSnapshot();
            return Ok(flights);
        }
    }
}