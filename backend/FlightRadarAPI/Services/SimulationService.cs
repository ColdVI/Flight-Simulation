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
        private readonly FlightPhysicsEngine _physicsEngine;
        private readonly FlightDataRecorder _dataRecorder;
        private readonly List<Flight> _flights = new();
        private readonly object _syncRoot = new();
        private volatile bool _isRunning = true;
        private double _speedMultiplier = 1.0;
        private DateTime _lastTick = DateTime.UtcNow;
        private DateTime _nextPersistence = DateTime.UtcNow;
        private static readonly TimeSpan PersistenceInterval = TimeSpan.FromSeconds(1);

        public SimulationService(
            IFlightRepository repository, 
            ILogger<SimulationService> logger,
            FlightDataRecorder dataRecorder)
        {
            _repository = repository;
            _logger = logger;
            _physicsEngine = new FlightPhysicsEngine();
            _dataRecorder = dataRecorder;
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
            
            _logger.LogInformation("Simulation service started with {Count} flights using realistic physics engine", flights.Count);

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
            _logger.LogInformation("Simulation resumed");
        }

        public void Stop()
        {
            _isRunning = false;
            _logger.LogInformation("Simulation paused");
        }

        public void SetSpeed(double multiplier)
        {
            var clamped = Math.Clamp(multiplier, 0.1, 1000.0);
            _speedMultiplier = clamped;
            _logger.LogInformation("Simulation speed set to {Speed}x", clamped);
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
                    // Reset all flight state
                    flight.Progress = 0;
                    flight.Status = "WAITING";
                    flight.Phase = FlightPhase.Preflight;
                    flight.CurrentLat = flight.OriginLat;
                    flight.CurrentLon = flight.OriginLon;
                    flight.Altitude = 0;
                    flight.Heading = 0;
                    flight.StartTime = now.AddSeconds(flight.StartOffsetSeconds);
                    
                    // Reset physics state
                    flight.Pitch = 0;
                    flight.Roll = 0;
                    flight.AngleOfAttack = 0;
                    flight.TrueAirspeed = 0;
                    flight.IndicatedAirspeed = 0;
                    flight.GroundSpeed = 0;
                    flight.VerticalSpeed = 0;
                    flight.Mach = 0;
                    flight.Throttle = 0;
                    flight.Thrust = 0;
                    flight.Drag = 0;
                    flight.Lift = 0;
                    
                    // Reset fuel
                    flight.FuelRemaining = 0; // Will be initialized by physics engine
                    flight.FuelConsumed = 0;
                    flight.FuelFlowRate = 0;
                    flight.GrossWeight = 0;
                    
                    // Reset statistics
                    flight.MaxAltitude = 0;
                    flight.MaxSpeed = 0;
                    flight.MaxMach = 0;
                    flight.MaxVerticalSpeed = 0;
                    flight.AverageSpeed = 0;
                    flight.AverageAltitude = 0;
                    flight.AverageFuelFlow = 0;
                    flight.TotalDistance = 0;
                    flight.DistanceFlown = 0;
                    flight.FlightTimeSeconds = 0;
                    flight.TimeInCurrentPhase = 0;
                    flight.TotalSamples = 0;
                    flight.LandingTime = null;
                }

                snapshot = _flights.ConvertAll(CloneFlight);
            }

            try
            {
                await _repository.ResetFlightsAsync(snapshot, cancellationToken);
                _logger.LogInformation("Simulation reset - {Count} flights returned to initial state", snapshot.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to reset flight state in the database.");
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Physics simulation loop started (50ms tick interval)");
            
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
                        _logger.LogError(ex, "Failed to persist flight state.");
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

            bool anyChanged = false;
            List<Flight> snapshot;

            lock (_syncRoot)
            {
                snapshot = new List<Flight>(_flights.Count);

                foreach (var flight in _flights)
                {
                    // Skip flights that haven't started yet
                    if (now < flight.StartTime && flight.Phase == FlightPhase.Preflight)
                    {
                        flight.Status = "WAITING";
                        snapshot.Add(CloneFlight(flight));
                        continue;
                    }

                    // Skip completed flights
                    if (flight.Phase == FlightPhase.Arrived)
                    {
                        snapshot.Add(CloneFlight(flight));
                        continue;
                    }

                    // Update flight using realistic physics engine
                    _physicsEngine.UpdateFlight(flight, deltaSeconds, _speedMultiplier);
                    anyChanged = true;

                    // Record telemetry sample for this flight
                    _dataRecorder.RecordSample(flight);
                    
                    // Generate report if flight just completed
                    if (flight.Phase == FlightPhase.Arrived && flight.LandingTime.HasValue)
                    {
                        var timeSinceLanding = DateTime.UtcNow - flight.LandingTime.Value;
                        if (timeSinceLanding < TimeSpan.FromSeconds(2))
                        {
                            _dataRecorder.GenerateReport(flight);
                        }
                    }

                    snapshot.Add(CloneFlight(flight));
                }
            }

            return anyChanged ? snapshot : null;
        }

        private static Flight CloneFlight(Flight source) => new Flight
        {
            // Identification
            Callsign = source.Callsign,
            From = source.From,
            To = source.To,
            OriginName = source.OriginName,
            DestinationName = source.DestinationName,
            AircraftTail = source.AircraftTail,
            AircraftModel = source.AircraftModel,
            AircraftManufacturer = source.AircraftManufacturer,
            
            // Position
            CurrentLat = source.CurrentLat,
            CurrentLon = source.CurrentLon,
            Altitude = source.Altitude,
            Heading = source.Heading,
            
            // Attitude
            Pitch = source.Pitch,
            Roll = source.Roll,
            AngleOfAttack = source.AngleOfAttack,
            
            // Velocities
            SpeedMs = source.SpeedMs,
            TrueAirspeed = source.TrueAirspeed,
            IndicatedAirspeed = source.IndicatedAirspeed,
            GroundSpeed = source.GroundSpeed,
            Mach = source.Mach,
            VerticalSpeed = source.VerticalSpeed,
            
            // Forces
            Thrust = source.Thrust,
            Drag = source.Drag,
            Lift = source.Lift,
            LiftToDragRatio = source.LiftToDragRatio,
            
            // Controls
            Throttle = source.Throttle,
            TargetAltitude = source.TargetAltitude,
            TargetSpeed = source.TargetSpeed,
            TargetHeading = source.TargetHeading,
            
            // Mass/Fuel
            GrossWeight = source.GrossWeight,
            FuelRemaining = source.FuelRemaining,
            FuelConsumed = source.FuelConsumed,
            FuelFlowRate = source.FuelFlowRate,
            
            // Phase/Status
            Phase = source.Phase,
            Status = source.Status,
            
            // Route
            OriginLat = source.OriginLat,
            OriginLon = source.OriginLon,
            DestLat = source.DestLat,
            DestLon = source.DestLon,
            StartTime = source.StartTime,
            StartOffsetSeconds = source.StartOffsetSeconds,
            Progress = source.Progress,
            
            // Distances
            TotalRouteDistance = source.TotalRouteDistance,
            DistanceFlown = source.DistanceFlown,
            DistanceRemaining = source.DistanceRemaining,
            
            // Statistics
            MaxAltitude = source.MaxAltitude,
            MaxSpeed = source.MaxSpeed,
            MaxMach = source.MaxMach,
            MaxVerticalSpeed = source.MaxVerticalSpeed,
            AverageSpeed = source.AverageSpeed,
            AverageAltitude = source.AverageAltitude,
            AverageFuelFlow = source.AverageFuelFlow,
            TotalDistance = source.TotalDistance,
            LandingTime = source.LandingTime,
            TotalSamples = source.TotalSamples,
            
            // Timing
            FlightTimeSeconds = source.FlightTimeSeconds,
            TimeInCurrentPhase = source.TimeInCurrentPhase
        };
    }
}