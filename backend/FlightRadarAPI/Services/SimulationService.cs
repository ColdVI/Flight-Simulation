using FlightRadarAPI.Data;
using FlightRadarAPI.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FlightRadarAPI.Services
{
    public class SimulationService : BackgroundService
    {
        private readonly IFlightRepository _repository;
        private readonly ILogger<SimulationService> _logger;
        private readonly List<Flight> _flights = new();
        private readonly object _syncRoot = new();
        private volatile bool _isRunning = true;
        private double _speedMultiplier = 1.0;
        private DateTime _lastTick = DateTime.UtcNow;
        private DateTime _nextPersistence = DateTime.UtcNow;
        private static readonly TimeSpan PersistenceInterval = TimeSpan.FromSeconds(1);

        public SimulationService(IFlightRepository repository, ILogger<SimulationService> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            await _repository.InitializeAsync(cancellationToken);
            var flights = await _repository.GetAllAsync(cancellationToken);

            lock (_syncRoot)
            {
                _flights.Clear();
                _flights.AddRange(flights);
            }

            _lastTick = DateTime.UtcNow;
            _nextPersistence = _lastTick.Add(PersistenceInterval);

            await base.StartAsync(cancellationToken);
        }

        public IReadOnlyList<Flight> GetFlightsSnapshot()
        {
            lock (_syncRoot)
            {
                return _flights.ConvertAll(CloneFlight);
            }
        }

        public Task<List<AirportSummary>> GetAirportsAsync(CancellationToken cancellationToken)
        {
            return _repository.GetAirportsAsync(cancellationToken);
        }

        public Task<List<AircraftSummary>> GetAircraftAsync(CancellationToken cancellationToken)
        {
            return _repository.GetAircraftAsync(cancellationToken);
        }

        public Task<List<FlightPlanSummary>> GetFlightPlansAsync(CancellationToken cancellationToken)
        {
            return _repository.GetFlightPlansAsync(cancellationToken);
        }

        public Task<FlightPlanSummary?> GetFlightPlanAsync(string callsign, CancellationToken cancellationToken)
        {
            return _repository.GetFlightPlanAsync(callsign, cancellationToken);
        }

        public async Task<FlightPlanSummary?> CreateFlightPlanAsync(FlightPlanRequest request, CancellationToken cancellationToken)
        {
            var created = await _repository.CreateFlightPlanAsync(request, cancellationToken);
            if (created?.Callsign is null)
            {
                return created;
            }

            var flight = await _repository.GetFlightAsync(created.Callsign, cancellationToken);
            if (flight is not null)
            {
                lock (_syncRoot)
                {
                    var existingIndex = _flights.FindIndex(f => f.Callsign.Equals(flight.Callsign, StringComparison.OrdinalIgnoreCase));
                    if (existingIndex >= 0)
                    {
                        _flights[existingIndex] = flight;
                    }
                    else
                    {
                        _flights.Add(flight);
                    }
                }
            }

            return created;
        }

        public void Start()
        {
            _isRunning = true;
            _lastTick = DateTime.UtcNow;
        }

        public void Stop() => _isRunning = false;

        public void SetSpeed(double multiplier)
        {
            var clamped = Math.Clamp(multiplier, 0.1, 10.0);
            _speedMultiplier = clamped;
        }

        public double GetSpeed() => _speedMultiplier;

        public async Task ResetAsync(CancellationToken cancellationToken)
        {
            List<Flight> snapshot;
            var now = DateTime.UtcNow;

            lock (_syncRoot)
            {
                foreach (var flight in _flights)
                {
                    flight.Progress = 0;
                    flight.Status = "WAITING";
                    flight.CurrentLat = flight.OriginLat;
                    flight.CurrentLon = flight.OriginLon;
                    flight.Altitude = 0;
                    flight.Heading = 0;
                    flight.StartTime = now.AddSeconds(flight.StartOffsetSeconds);
                }

                snapshot = _flights.ConvertAll(CloneFlight);
            }

            try
            {
                await _repository.ResetFlightsAsync(snapshot, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset flight state in the database.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                List<Flight>? snapshot = null;

                if (_isRunning)
                {
                    snapshot = UpdatePhysics();
                }

                if (snapshot != null && DateTime.UtcNow >= _nextPersistence)
                {
                    try
                    {
                        await _repository.BulkUpsertAsync(snapshot, stoppingToken);
                        _nextPersistence = DateTime.UtcNow.Add(PersistenceInterval);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to persist flight state to PostgreSQL.");
                    }
                }

                await Task.Delay(50, stoppingToken);
            }
        }

        private List<Flight>? UpdatePhysics()
        {
            var now = DateTime.UtcNow;
            var deltaSeconds = (now - _lastTick).TotalSeconds;
            if (deltaSeconds <= 0)
            {
                deltaSeconds = 0.05;
            }

            _lastTick = now;
            var normalizedDelta = deltaSeconds / 0.05; // 50ms referans döngü

            bool anyChanged = false;
            List<Flight> snapshot;

            lock (_syncRoot)
            {
                snapshot = new List<Flight>(_flights.Count);

                foreach (var flight in _flights)
                {
                    if (now < flight.StartTime)
                    {
                        flight.Status = "WAITING";
                        flight.Altitude = 0;
                        snapshot.Add(CloneFlight(flight));
                        continue;
                    }

                    if (flight.Progress < 1.0)
                    {
                        flight.Status = "ACTIVE";
                        double speedFactor = (flight.SpeedMs / 500000.0) * _speedMultiplier * normalizedDelta;
                        flight.Progress = Math.Min(1.0, flight.Progress + speedFactor);

                        flight.CurrentLat = flight.OriginLat + (flight.DestLat - flight.OriginLat) * flight.Progress;
                        flight.CurrentLon = flight.OriginLon + (flight.DestLon - flight.OriginLon) * flight.Progress;
                        flight.Altitude = Math.Max(0, 10000 * Math.Sin(flight.Progress * Math.PI));
                        flight.Heading = Math.Atan2(flight.DestLon - flight.OriginLon, flight.DestLat - flight.OriginLat);

                        anyChanged = true;
                    }

                    if (flight.Progress >= 1.0)
                    {
                        flight.Progress = 1.0;
                        flight.Status = "LANDED";
                        flight.CurrentLat = flight.DestLat;
                        flight.CurrentLon = flight.DestLon;
                        flight.Altitude = 0;
                    }

                    snapshot.Add(CloneFlight(flight));
                }
            }

            return anyChanged ? snapshot : null;
        }

        private static Flight CloneFlight(Flight source) => new Flight
        {
            Callsign = source.Callsign,
            From = source.From,
            To = source.To,
            OriginName = source.OriginName,
            DestinationName = source.DestinationName,
            AircraftTail = source.AircraftTail,
            AircraftModel = source.AircraftModel,
            AircraftManufacturer = source.AircraftManufacturer,
            CurrentLat = source.CurrentLat,
            CurrentLon = source.CurrentLon,
            Altitude = source.Altitude,
            Heading = source.Heading,
            SpeedMs = source.SpeedMs,
            Status = source.Status,
            OriginLat = source.OriginLat,
            OriginLon = source.OriginLon,
            DestLat = source.DestLat,
            DestLon = source.DestLon,
            StartTime = source.StartTime,
            StartOffsetSeconds = source.StartOffsetSeconds,
            Progress = source.Progress
        };
    }
}