using System.Collections.Concurrent;
using System.Text.Json;
using FlightRadarAPI.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlightRadarAPI.Data
{
    public class InMemoryFlightRepository : IFlightRepository
    {
        private readonly ILogger<InMemoryFlightRepository> _logger;
        private readonly IWebHostEnvironment _environment;

        private readonly ConcurrentDictionary<string, Flight> _flights = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, AirportSummary> _airports = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, AircraftSummary> _aircraft = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FlightPlanSummary> _flightPlans = new(StringComparer.OrdinalIgnoreCase);

        public InMemoryFlightRepository(ILogger<InMemoryFlightRepository> logger, IWebHostEnvironment environment)
        {
            _logger = logger;
            _environment = environment;
        }

        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Initializing In-Memory Repository...");

            // Load Airports
            var airportSeeds = await LoadSeedAsync<AirportSeedDto>("airports.json", cancellationToken);
            foreach (var seed in airportSeeds)
            {
                _airports[seed.Code] = new AirportSummary
                {
                    Code = seed.Code,
                    Name = seed.Name,
                    City = seed.City,
                    Country = seed.Country,
                    Latitude = seed.Latitude,
                    Longitude = seed.Longitude
                };
            }

            // Load Aircraft
            var aircraftSeeds = await LoadSeedAsync<AircraftSeedDto>("aircraft.json", cancellationToken);
            foreach (var seed in aircraftSeeds)
            {
                _aircraft[seed.TailNumber] = new AircraftSummary
                {
                    TailNumber = seed.TailNumber,
                    Model = seed.Model,
                    Manufacturer = seed.Manufacturer,
                    CruiseSpeedMs = seed.CruiseSpeedMs,
                    IsAvailable = true
                };
            }

            // Load Flights
            var flightSeeds = await LoadSeedAsync<FlightSeedDto>("flights.json", cancellationToken);
            var now = DateTime.UtcNow;

            foreach (var seed in flightSeeds)
            {
                if (!_airports.TryGetValue(seed.From, out var origin) || !_airports.TryGetValue(seed.To, out var dest))
                {
                    continue;
                }

                if (!_aircraft.TryGetValue(seed.AircraftTail, out var ac))
                {
                    continue;
                }

                var flight = new Flight
                {
                    Callsign = seed.Callsign,
                    From = seed.From,
                    To = seed.To,
                    OriginName = origin.Name,
                    DestinationName = dest.Name,
                    OriginLat = origin.Latitude,
                    OriginLon = origin.Longitude,
                    DestLat = dest.Latitude,
                    DestLon = dest.Longitude,
                    AircraftTail = seed.AircraftTail,
                    AircraftModel = ac.Model,
                    AircraftManufacturer = ac.Manufacturer,
                    SpeedMs = seed.SpeedMs,
                    StartOffsetSeconds = seed.StartTimeOffset,
                    StartTime = now.AddSeconds(seed.StartTimeOffset),
                    Status = "WAITING",
                    CurrentLat = origin.Latitude,
                    CurrentLon = origin.Longitude,
                    Altitude = 0,
                    Heading = Math.Atan2(dest.Longitude - origin.Longitude, dest.Latitude - origin.Latitude),
                    Progress = 0
                };

                _flights[flight.Callsign] = flight;
                
                // Also create a plan entry
                _flightPlans[flight.Callsign] = new FlightPlanSummary
                {
                    Callsign = flight.Callsign,
                    AircraftTail = flight.AircraftTail,
                    OriginCode = flight.From,
                    DestinationCode = flight.To,
                    PlannedSpeedMs = flight.SpeedMs,
                    StartTimeUtc = flight.StartTime,
                    Status = flight.Status,
                    Progress = flight.Progress
                };
            }
            
            _logger.LogInformation("In-Memory Repository Initialized with {Count} flights.", _flights.Count);
        }

        public Task<List<Flight>> GetAllAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_flights.Values.ToList());
        }

        public Task BulkUpsertAsync(IEnumerable<Flight> flights, CancellationToken cancellationToken)
        {
            foreach (var flight in flights)
            {
                _flights[flight.Callsign] = flight;
                
                if (_flightPlans.TryGetValue(flight.Callsign, out var plan))
                {
                    plan.Status = flight.Status;
                    plan.Progress = flight.Progress;
                }
            }
            return Task.CompletedTask;
        }

        public Task ResetFlightsAsync(IEnumerable<Flight> flights, CancellationToken cancellationToken)
        {
            foreach (var flight in flights)
            {
                _flights[flight.Callsign] = flight;
            }
            return Task.CompletedTask;
        }

        public Task<List<AirportSummary>> GetAirportsAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_airports.Values.OrderBy(a => a.Name).ToList());
        }

        public Task<List<AircraftSummary>> GetAircraftAsync(CancellationToken cancellationToken)
        {
            // Simple availability check
            var activeTails = _flights.Values
                .Where(f => f.Status == "ACTIVE" || f.Status == "WAITING")
                .Select(f => f.AircraftTail)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var result = _aircraft.Values.Select(a => new AircraftSummary
            {
                TailNumber = a.TailNumber,
                Model = a.Model,
                Manufacturer = a.Manufacturer,
                CruiseSpeedMs = a.CruiseSpeedMs,
                IsAvailable = !activeTails.Contains(a.TailNumber)
            }).OrderBy(a => a.TailNumber).ToList();

            return Task.FromResult(result);
        }

        public Task<List<FlightPlanSummary>> GetFlightPlansAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_flightPlans.Values.OrderBy(p => p.StartTimeUtc).ToList());
        }

        public Task<FlightPlanSummary?> GetFlightPlanAsync(string callsign, CancellationToken cancellationToken)
        {
            _flightPlans.TryGetValue(callsign, out var plan);
            return Task.FromResult(plan);
        }

        public Task<FlightPlanSummary?> CreateFlightPlanAsync(FlightPlanRequest request, CancellationToken cancellationToken)
        {
            if (_flightPlans.ContainsKey(request.Callsign))
            {
                throw new InvalidOperationException($"Flight plan {request.Callsign} already exists.");
            }

            if (!_airports.TryGetValue(request.OriginCode, out var origin) || !_airports.TryGetValue(request.DestinationCode, out var dest))
            {
                throw new InvalidOperationException("Invalid airports.");
            }
            
            if (!_aircraft.TryGetValue(request.AircraftTail, out var ac))
            {
                throw new InvalidOperationException("Invalid aircraft.");
            }

            var now = DateTime.UtcNow;
            var startTime = request.StartTimeUtc < now ? now.AddSeconds(5) : request.StartTimeUtc;
            var offset = (int)(startTime - now).TotalSeconds;
            var speed = request.PlannedSpeedMs ?? ac.CruiseSpeedMs;

            var flight = new Flight
            {
                Callsign = request.Callsign,
                From = request.OriginCode,
                To = request.DestinationCode,
                OriginName = origin.Name,
                DestinationName = dest.Name,
                OriginLat = origin.Latitude,
                OriginLon = origin.Longitude,
                DestLat = dest.Latitude,
                DestLon = dest.Longitude,
                AircraftTail = request.AircraftTail,
                AircraftModel = ac.Model,
                AircraftManufacturer = ac.Manufacturer,
                SpeedMs = speed,
                StartOffsetSeconds = offset,
                StartTime = startTime,
                Status = "WAITING",
                CurrentLat = origin.Latitude,
                CurrentLon = origin.Longitude,
                Altitude = 0,
                Heading = Math.Atan2(dest.Longitude - origin.Longitude, dest.Latitude - origin.Latitude),
                Progress = 0
            };

            _flights[flight.Callsign] = flight;

            var plan = new FlightPlanSummary
            {
                Callsign = flight.Callsign,
                AircraftTail = flight.AircraftTail,
                OriginCode = flight.From,
                DestinationCode = flight.To,
                PlannedSpeedMs = flight.SpeedMs,
                StartTimeUtc = flight.StartTime,
                Status = flight.Status,
                Progress = flight.Progress
            };
            _flightPlans[flight.Callsign] = plan;

            return Task.FromResult<FlightPlanSummary?>(plan);
        }

        public Task<Flight?> GetFlightAsync(string callsign, CancellationToken cancellationToken)
        {
            _flights.TryGetValue(callsign, out var flight);
            return Task.FromResult(flight);
        }

        private async Task<List<T>> LoadSeedAsync<T>(string fileName, CancellationToken cancellationToken)
        {
            var datasetPath = Path.Combine(_environment.ContentRootPath, "Data", fileName);
            if (!File.Exists(datasetPath)) return new List<T>();

            using var stream = File.OpenRead(datasetPath);
            return await JsonSerializer.DeserializeAsync<List<T>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken) ?? new List<T>();
        }

        private class AirportSeedDto
        {
            public string Code { get; set; } = "";
            public string Name { get; set; } = "";
            public string? City { get; set; }
            public string? Country { get; set; }
            public double Latitude { get; set; }
            public double Longitude { get; set; }
        }

        private class AircraftSeedDto
        {
            public string TailNumber { get; set; } = "";
            public string Model { get; set; } = "";
            public string? Manufacturer { get; set; }
            public double CruiseSpeedMs { get; set; }
        }

        private class FlightSeedDto
        {
            public string Callsign { get; set; } = "";
            public string From { get; set; } = "";
            public string To { get; set; } = "";
            public string AircraftTail { get; set; } = "";
            public double SpeedMs { get; set; }
            public int StartTimeOffset { get; set; }
        }
    }
}
