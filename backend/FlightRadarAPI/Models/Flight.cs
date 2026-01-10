namespace FlightRadarAPI.Models
{
    /// <summary>
    /// Represents the current flight phase in a realistic flight simulation.
    /// </summary>
    public enum FlightPhase
    {
        Preflight,      // Before departure
        Taxi,           // Ground movement
        Takeoff,        // Takeoff roll and initial climb
        Climb,          // Climbing to cruise altitude
        Cruise,         // Level flight at cruise altitude
        Descent,        // Descending from cruise
        Approach,       // Final approach phase
        Landing,        // Landing and rollout
        Arrived         // Flight complete
    }

    public class Flight
    {
        // ===== Identification =====
        public string Callsign { get; set; } = string.Empty;
        public string From { get; set; } = string.Empty;
        public string To { get; set; } = string.Empty;
        public string OriginName { get; set; } = string.Empty;
        public string DestinationName { get; set; } = string.Empty;
        public string AircraftTail { get; set; } = string.Empty;
        public string AircraftModel { get; set; } = string.Empty;
        public string AircraftManufacturer { get; set; } = string.Empty;
        
        // ===== Position (Geodetic) =====
        public double CurrentLat { get; set; }
        public double CurrentLon { get; set; }
        public double Altitude { get; set; }              // Geometric altitude in meters
        public double Heading { get; set; }               // True heading in radians
        
        // ===== Attitude (Body Angles) =====
        public double Pitch { get; set; }                 // Nose up/down in radians
        public double Roll { get; set; }                  // Bank angle in radians
        public double AngleOfAttack { get; set; }         // AoA in radians
        
        // ===== Velocities =====
        public double SpeedMs { get; set; }               // Ground speed in m/s (legacy, kept for compatibility)
        public double TrueAirspeed { get; set; }          // TAS in m/s
        public double IndicatedAirspeed { get; set; }     // IAS in m/s
        public double GroundSpeed { get; set; }           // Ground speed in m/s
        public double Mach { get; set; }                  // Mach number
        public double VerticalSpeed { get; set; }         // Climb/descent rate in m/s (positive = climb)
        
        // ===== Forces (Calculated) =====
        public double Thrust { get; set; }                // Current thrust in Newtons
        public double Drag { get; set; }                  // Current drag in Newtons
        public double Lift { get; set; }                  // Current lift in Newtons
        public double LiftToDragRatio { get; set; }       // L/D ratio
        
        // ===== Controls / Autopilot =====
        public double Throttle { get; set; }              // 0.0 to 1.0
        public double TargetAltitude { get; set; }        // Autopilot target altitude in meters
        public double TargetSpeed { get; set; }           // Autopilot target speed in m/s (TAS or Mach)
        public double TargetHeading { get; set; }         // Autopilot target heading in radians
        
        // ===== Mass / Fuel =====
        public double GrossWeight { get; set; }           // Current total weight in kg
        public double FuelRemaining { get; set; }         // Fuel in kg
        public double FuelConsumed { get; set; }          // Total fuel consumed in kg
        public double FuelFlowRate { get; set; }          // Current fuel flow in kg/s
        
        // ===== Flight Phase =====
        public FlightPhase Phase { get; set; } = FlightPhase.Preflight;
        public string Status { get; set; } = "WAITING";   // Legacy status for compatibility

        // ===== Route Data =====
        public double OriginLat { get; set; }
        public double OriginLon { get; set; }
        public double DestLat { get; set; }
        public double DestLon { get; set; }
        
        public DateTime StartTime { get; set; }
        public int StartOffsetSeconds { get; set; }
        public double Progress { get; set; }              // 0.0 to 1.0 route progress
        
        // ===== Distance Tracking =====
        public double TotalRouteDistance { get; set; }    // Great circle distance origin to dest in meters
        public double DistanceFlown { get; set; }         // Actual distance traveled in meters
        public double DistanceRemaining { get; set; }     // Remaining distance to destination in meters
        
        // ===== Flight Statistics =====
        public double MaxAltitude { get; set; } = 0;
        public double MaxSpeed { get; set; } = 0;
        public double MaxMach { get; set; } = 0;
        public double MaxVerticalSpeed { get; set; } = 0;
        public double AverageSpeed { get; set; } = 0;
        public double AverageAltitude { get; set; } = 0;
        public double AverageFuelFlow { get; set; } = 0;
        public double TotalDistance { get; set; } = 0;    // Legacy field
        public DateTime? LandingTime { get; set; }
        public int TotalSamples { get; set; } = 0;
        
        // ===== Timing =====
        public double FlightTimeSeconds { get; set; }     // Total flight time in seconds
        public double TimeInCurrentPhase { get; set; }    // Seconds in current flight phase
        
        // ===== Computed Properties =====
        
        /// <summary>Flight time formatted as HH:MM:SS</summary>
        public string FlightTimeFormatted => TimeSpan.FromSeconds(FlightTimeSeconds).ToString(@"hh\:mm\:ss");
        
        /// <summary>Heading in degrees (0-360)</summary>
        public double HeadingDegrees => ((Heading * 180.0 / Math.PI) + 360) % 360;
        
        /// <summary>Pitch in degrees</summary>
        public double PitchDegrees => Pitch * 180.0 / Math.PI;
        
        /// <summary>Roll in degrees</summary>  
        public double RollDegrees => Roll * 180.0 / Math.PI;
        
        /// <summary>Vertical speed in feet per minute</summary>
        public double VerticalSpeedFpm => VerticalSpeed * 196.85; // m/s to ft/min
        
        /// <summary>Altitude in feet</summary>
        public double AltitudeFeet => Altitude * 3.28084;
        
        /// <summary>Speed in knots (TAS)</summary>
        public double SpeedKnots => TrueAirspeed * 1.94384;
    }
}