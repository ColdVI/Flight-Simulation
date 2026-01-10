namespace FlightRadarAPI.Models
{
    /// <summary>
    /// Defines the aerodynamic and performance characteristics of an aircraft type.
    /// Used by the physics engine to calculate realistic flight behavior.
    /// </summary>
    public class AircraftPerformance
    {
        public string Model { get; set; } = string.Empty;
        public string Manufacturer { get; set; } = string.Empty;

        // Physical Dimensions
        public double WingArea { get; set; }           // m² - Reference wing area
        public double WingSpan { get; set; }           // m
        public double AspectRatio => WingSpan * WingSpan / WingArea;

        // Mass Properties
        public double EmptyWeight { get; set; }        // kg - Operating empty weight
        public double MaxFuel { get; set; }            // kg - Maximum fuel capacity
        public double MaxTakeoffWeight { get; set; }   // kg - MTOW
        public double MaxLandingWeight { get; set; }   // kg - MLW

        // Aerodynamic Coefficients
        public double Cd0 { get; set; }                // Zero-lift drag coefficient
        public double OswaldFactor { get; set; }       // Oswald efficiency factor (e)
        public double ClMax { get; set; }              // Maximum lift coefficient (clean)
        public double ClMaxFlaps { get; set; }         // Maximum lift coefficient (flaps extended)

        // Propulsion
        public double MaxThrustSeaLevel { get; set; }  // N - Maximum thrust at sea level
        public double ThrustLapseRate { get; set; }    // Thrust reduction factor with altitude
        public double TSFC { get; set; }               // Thrust-specific fuel consumption (kg/N/s)
        public int NumberOfEngines { get; set; }

        // Performance Limits
        public double ServiceCeiling { get; set; }     // m - Maximum operating altitude
        public double CruiseAltitude { get; set; }     // m - Typical cruise altitude
        public double CruiseMach { get; set; }         // Mach - Typical cruise Mach
        public double MaxMach { get; set; }            // Mach - MMO (Max Mach Operating)
        public double VNE { get; set; }                // m/s - Never exceed speed (IAS)
        public double VS0 { get; set; }                // m/s - Stall speed (clean, at MTOW)
        public double VS1 { get; set; }                // m/s - Stall speed (landing config)

        // Climb/Descent Performance
        public double MaxClimbRate { get; set; }       // m/s - Maximum climb rate at sea level
        public double MaxDescentRate { get; set; }     // m/s - Maximum descent rate
        public double ClimbAngleMax { get; set; }      // radians - Maximum climb angle

        // Turn Performance
        public double MaxBankAngle { get; set; }       // radians - Maximum bank angle (typically 25-30°)
        public double MaxLoadFactor { get; set; }      // G - Maximum load factor (typically 2.5)

        /// <summary>
        /// Gets predefined performance data for common aircraft types.
        /// </summary>
        public static Dictionary<string, AircraftPerformance> GetPresets()
        {
            return new Dictionary<string, AircraftPerformance>(StringComparer.OrdinalIgnoreCase)
            {
                ["Airbus A350-900"] = new AircraftPerformance
                {
                    Model = "A350-900",
                    Manufacturer = "Airbus",
                    WingArea = 442.0,
                    WingSpan = 64.75,
                    EmptyWeight = 142400,
                    MaxFuel = 141000,
                    MaxTakeoffWeight = 280000,
                    MaxLandingWeight = 207000,
                    Cd0 = 0.018,
                    OswaldFactor = 0.85,
                    ClMax = 1.6,
                    ClMaxFlaps = 2.5,
                    MaxThrustSeaLevel = 2 * 374000, // 2x Trent XWB-84 engines
                    ThrustLapseRate = 0.25,
                    TSFC = 0.0000145, // kg/N/s (approximately 0.52 lb/lbf/hr)
                    NumberOfEngines = 2,
                    ServiceCeiling = 13100,
                    CruiseAltitude = 11900,
                    CruiseMach = 0.85,
                    MaxMach = 0.89,
                    VNE = 340,
                    VS0 = 75,
                    VS1 = 68,
                    MaxClimbRate = 15.0,
                    MaxDescentRate = 20.0,
                    ClimbAngleMax = 0.15,
                    MaxBankAngle = 0.44, // ~25 degrees
                    MaxLoadFactor = 2.5
                },
                ["Airbus A380-800"] = new AircraftPerformance
                {
                    Model = "A380-800",
                    Manufacturer = "Airbus",
                    WingArea = 845.0,
                    WingSpan = 79.75,
                    EmptyWeight = 276800,
                    MaxFuel = 320000,
                    MaxTakeoffWeight = 575000,
                    MaxLandingWeight = 394000,
                    Cd0 = 0.020,
                    OswaldFactor = 0.82,
                    ClMax = 1.5,
                    ClMaxFlaps = 2.4,
                    MaxThrustSeaLevel = 4 * 311000, // 4x GP7200 or Trent 900
                    ThrustLapseRate = 0.26,
                    TSFC = 0.0000155,
                    NumberOfEngines = 4,
                    ServiceCeiling = 13100,
                    CruiseAltitude = 11600,
                    CruiseMach = 0.85,
                    MaxMach = 0.89,
                    VNE = 340,
                    VS0 = 85,
                    VS1 = 78,
                    MaxClimbRate = 12.0,
                    MaxDescentRate = 18.0,
                    ClimbAngleMax = 0.12,
                    MaxBankAngle = 0.44,
                    MaxLoadFactor = 2.5
                },
                ["Boeing 747-8I"] = new AircraftPerformance
                {
                    Model = "747-8I",
                    Manufacturer = "Boeing",
                    WingArea = 554.0,
                    WingSpan = 68.4,
                    EmptyWeight = 214100,
                    MaxFuel = 216840,
                    MaxTakeoffWeight = 447700,
                    MaxLandingWeight = 312100,
                    Cd0 = 0.019,
                    OswaldFactor = 0.80,
                    ClMax = 1.5,
                    ClMaxFlaps = 2.3,
                    MaxThrustSeaLevel = 4 * 296000, // 4x GEnx-2B67
                    ThrustLapseRate = 0.25,
                    TSFC = 0.0000148,
                    NumberOfEngines = 4,
                    ServiceCeiling = 13100,
                    CruiseAltitude = 10700,
                    CruiseMach = 0.855,
                    MaxMach = 0.90,
                    VNE = 350,
                    VS0 = 80,
                    VS1 = 72,
                    MaxClimbRate = 13.0,
                    MaxDescentRate = 18.0,
                    ClimbAngleMax = 0.13,
                    MaxBankAngle = 0.44,
                    MaxLoadFactor = 2.5
                },
                ["Boeing 777-300ER"] = new AircraftPerformance
                {
                    Model = "777-300ER",
                    Manufacturer = "Boeing",
                    WingArea = 427.8,
                    WingSpan = 64.8,
                    EmptyWeight = 167800,
                    MaxFuel = 145538,
                    MaxTakeoffWeight = 351500,
                    MaxLandingWeight = 251300,
                    Cd0 = 0.018,
                    OswaldFactor = 0.84,
                    ClMax = 1.55,
                    ClMaxFlaps = 2.4,
                    MaxThrustSeaLevel = 2 * 513000, // 2x GE90-115B
                    ThrustLapseRate = 0.24,
                    TSFC = 0.0000142,
                    NumberOfEngines = 2,
                    ServiceCeiling = 13100,
                    CruiseAltitude = 10700,
                    CruiseMach = 0.84,
                    MaxMach = 0.89,
                    VNE = 340,
                    VS0 = 78,
                    VS1 = 70,
                    MaxClimbRate = 14.0,
                    MaxDescentRate = 18.0,
                    ClimbAngleMax = 0.14,
                    MaxBankAngle = 0.44,
                    MaxLoadFactor = 2.5
                },
                ["Boeing 737-900"] = new AircraftPerformance
                {
                    Model = "737-900",
                    Manufacturer = "Boeing",
                    WingArea = 124.6,
                    WingSpan = 34.3,
                    EmptyWeight = 44700,
                    MaxFuel = 20894,
                    MaxTakeoffWeight = 85100,
                    MaxLandingWeight = 71400,
                    Cd0 = 0.022,
                    OswaldFactor = 0.78,
                    ClMax = 1.4,
                    ClMaxFlaps = 2.2,
                    MaxThrustSeaLevel = 2 * 121400, // 2x CFM56-7B27
                    ThrustLapseRate = 0.22,
                    TSFC = 0.0000158,
                    NumberOfEngines = 2,
                    ServiceCeiling = 12500,
                    CruiseAltitude = 10700,
                    CruiseMach = 0.785,
                    MaxMach = 0.82,
                    VNE = 310,
                    VS0 = 65,
                    VS1 = 58,
                    MaxClimbRate = 16.0,
                    MaxDescentRate = 20.0,
                    ClimbAngleMax = 0.16,
                    MaxBankAngle = 0.44,
                    MaxLoadFactor = 2.5
                },
                ["Boeing 777-200ER"] = new AircraftPerformance
                {
                    Model = "777-200ER",
                    Manufacturer = "Boeing",
                    WingArea = 427.8,
                    WingSpan = 60.9,
                    EmptyWeight = 138100,
                    MaxFuel = 117340,
                    MaxTakeoffWeight = 297550,
                    MaxLandingWeight = 213000,
                    Cd0 = 0.019,
                    OswaldFactor = 0.82,
                    ClMax = 1.5,
                    ClMaxFlaps = 2.3,
                    MaxThrustSeaLevel = 2 * 417000, // 2x GE90-94B
                    ThrustLapseRate = 0.24,
                    TSFC = 0.0000145,
                    NumberOfEngines = 2,
                    ServiceCeiling = 13100,
                    CruiseAltitude = 10700,
                    CruiseMach = 0.84,
                    MaxMach = 0.89,
                    VNE = 340,
                    VS0 = 75,
                    VS1 = 68,
                    MaxClimbRate = 14.5,
                    MaxDescentRate = 18.0,
                    ClimbAngleMax = 0.14,
                    MaxBankAngle = 0.44,
                    MaxLoadFactor = 2.5
                }
            };
        }

        /// <summary>
        /// Gets the best matching performance data for an aircraft model.
        /// Falls back to A350-900 if no match found.
        /// </summary>
        public static AircraftPerformance GetForModel(string model)
        {
            var presets = GetPresets();
            
            // Try exact match first
            if (presets.TryGetValue(model, out var exact))
                return exact;

            // Try partial match
            foreach (var kvp in presets)
            {
                if (model.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase) ||
                    kvp.Key.Contains(model, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Value;
                }
            }

            // Default to A350-900
            return presets["Airbus A350-900"];
        }
    }
}
