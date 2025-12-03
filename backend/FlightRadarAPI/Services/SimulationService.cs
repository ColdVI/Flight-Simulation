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

                        // Great Circle Interpolation
                        double lat1 = flight.OriginLat * Math.PI / 180.0;
                        double lon1 = flight.OriginLon * Math.PI / 180.0;
                        double lat2 = flight.DestLat * Math.PI / 180.0;
                        double lon2 = flight.DestLon * Math.PI / 180.0;

                        // Calculate total central angle (delta)
                        double dLat = lat2 - lat1;
                        double dLon = lon2 - lon1;
                        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                                   Math.Cos(lat1) * Math.Cos(lat2) *
                                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                        double delta = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

                        if (delta == 0)
                        {
                            flight.CurrentLat = flight.DestLat;
                            flight.CurrentLon = flight.DestLon;
                        }
                        else
                        {
                            // Interpolate
                            double A = Math.Sin((1 - flight.Progress) * delta) / Math.Sin(delta);
                            double B = Math.Sin(flight.Progress * delta) / Math.Sin(delta);

                            double x = A * Math.Cos(lat1) * Math.Cos(lon1) + B * Math.Cos(lat2) * Math.Cos(lon2);
                            double y = A * Math.Cos(lat1) * Math.Sin(lon1) + B * Math.Cos(lat2) * Math.Sin(lon2);
                            double z = A * Math.Sin(lat1) + B * Math.Sin(lat2);

                            double newLatRad = Math.Atan2(z, Math.Sqrt(x * x + y * y));
                            double newLonRad = Math.Atan2(y, x);

                            flight.CurrentLat = newLatRad * 180.0 / Math.PI;
                            flight.CurrentLon = newLonRad * 180.0 / Math.PI;

                            // Calculate dynamic heading for Great Circle
                            double yHeading = Math.Sin(lon2 - newLonRad) * Math.Cos(lat2);
                            double xHeading = Math.Cos(newLatRad) * Math.Sin(lat2) -
                                              Math.Sin(newLatRad) * Math.Cos(lat2) * Math.Cos(lon2 - newLonRad);
                            flight.Heading = Math.Atan2(yHeading, xHeading);
                        }

                        // Realistic altitude profile: climb to cruising (0-70% of journey), then descend (70-100%)
                        double altitudeProfile;
                        if (flight.Progress < 0.7)
                        {
                            // Climb phase: 0-70% of journey
                            double climbProgress = flight.Progress / 0.7;
                            altitudeProfile = 10500 * Math.Sin(climbProgress * Math.PI / 2); // 0 to 10500m
                        }
                        else
                        {
                            // Descent phase: 70-100% of journey
                            double descentProgress = (flight.Progress - 0.7) / 0.3;
                            altitudeProfile = 10500 * Math.Cos(descentProgress * Math.PI / 2); // 10500 to 0m
                        }
                        
                        flight.Altitude = Math.Max(0, altitudeProfile);
                        
                        // Update statistics
                        flight.MaxAltitude = Math.Max(flight.MaxAltitude, flight.Altitude);
                        flight.MaxSpeed = Math.Max(flight.MaxSpeed, flight.SpeedMs);
                        flight.AverageSpeed = ((flight.AverageSpeed * flight.TotalSamples) + flight.SpeedMs) / (flight.TotalSamples + 1);
                        flight.AverageAltitude = ((flight.AverageAltitude * flight.TotalSamples) + flight.Altitude) / (flight.TotalSamples + 1);
                        flight.TotalSamples++;
                        flight.TotalDistance += Math.Abs(flight.SpeedMs * deltaSeconds);

                        anyChanged = true;
                    }

                    if (flight.Progress >= 1.0)
                    {
                        flight.Progress = 1.0;
                        flight.Status = "LANDED";
                        flight.CurrentLat = flight.DestLat;
                        flight.CurrentLon = flight.DestLon;
                        flight.Altitude = 0;
                        flight.LandingTime = DateTime.UtcNow;
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
            Progress = source.Progress,
            MaxAltitude = source.MaxAltitude,
            MaxSpeed = source.MaxSpeed,
            AverageSpeed = source.AverageSpeed,
            AverageAltitude = source.AverageAltitude,
            TotalDistance = source.TotalDistance,
            LandingTime = source.LandingTime,
            TotalSamples = source.TotalSamples
        };
    }
}