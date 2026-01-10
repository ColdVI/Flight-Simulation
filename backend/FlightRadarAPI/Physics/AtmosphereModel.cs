namespace FlightRadarAPI.Physics
{
    /// <summary>
    /// International Standard Atmosphere (ISA) model for calculating atmospheric properties at altitude.
    /// Implements the ICAO standard atmosphere up to 86km.
    /// </summary>
    public static class AtmosphereModel
    {
        // ISA Sea Level Reference Values
        public const double SeaLevelTemperature = 288.15;    // K (15°C)
        public const double SeaLevelPressure = 101325.0;     // Pa
        public const double SeaLevelDensity = 1.225;         // kg/m³
        public const double SeaLevelSpeedOfSound = 340.29;   // m/s

        // Physical Constants
        public const double GravitationalAcceleration = 9.80665;  // m/s²
        public const double GasConstant = 287.05287;              // J/(kg·K) for dry air
        public const double SpecificHeatRatio = 1.4;              // γ for air
        public const double MolarMass = 0.0289644;                // kg/mol

        // Temperature Lapse Rates (K/m) for different layers
        private static readonly (double Altitude, double LapseRate, double BaseTemperature)[] Layers = new[]
        {
            (0.0,      -0.0065, 288.15),   // Troposphere (0-11km)
            (11000.0,  0.0,     216.65),   // Tropopause (11-20km)
            (20000.0,  0.001,   216.65),   // Stratosphere lower (20-32km)
            (32000.0,  0.0028,  228.65),   // Stratosphere upper (32-47km)
            (47000.0,  0.0,     270.65),   // Stratopause (47-51km)
            (51000.0,  -0.0028, 270.65),   // Mesosphere lower (51-71km)
            (71000.0,  -0.002,  214.65),   // Mesosphere upper (71-86km)
        };

        /// <summary>
        /// Calculates atmospheric properties at a given geometric altitude.
        /// </summary>
        /// <param name="altitude">Geometric altitude in meters</param>
        /// <returns>Atmospheric properties at the specified altitude</returns>
        public static AtmosphereState GetAtmosphereAt(double altitude)
        {
            altitude = Math.Max(0, Math.Min(altitude, 86000)); // Clamp to valid range

            var (temperature, pressure) = CalculateTemperatureAndPressure(altitude);
            var density = pressure / (GasConstant * temperature);
            var speedOfSound = Math.Sqrt(SpecificHeatRatio * GasConstant * temperature);

            return new AtmosphereState
            {
                Altitude = altitude,
                Temperature = temperature,
                Pressure = pressure,
                Density = density,
                SpeedOfSound = speedOfSound,
                DensityRatio = density / SeaLevelDensity,
                PressureRatio = pressure / SeaLevelPressure,
                TemperatureRatio = temperature / SeaLevelTemperature
            };
        }

        private static (double Temperature, double Pressure) CalculateTemperatureAndPressure(double altitude)
        {
            double temperature = SeaLevelTemperature;
            double pressure = SeaLevelPressure;
            double currentAltitude = 0;

            for (int i = 0; i < Layers.Length; i++)
            {
                var (layerBase, lapseRate, baseTemp) = Layers[i];
                double layerTop = i < Layers.Length - 1 ? Layers[i + 1].Altitude : 86000;

                if (altitude <= layerTop)
                {
                    double deltaH = altitude - layerBase;
                    temperature = baseTemp + lapseRate * deltaH;

                    if (Math.Abs(lapseRate) < 1e-10)
                    {
                        // Isothermal layer
                        pressure *= Math.Exp(-GravitationalAcceleration * deltaH / (GasConstant * baseTemp));
                    }
                    else
                    {
                        // Temperature gradient layer
                        double exponent = -GravitationalAcceleration / (lapseRate * GasConstant);
                        pressure *= Math.Pow(temperature / baseTemp, exponent);
                    }
                    break;
                }
                else
                {
                    // Calculate pressure at top of this layer
                    double deltaH = layerTop - layerBase;
                    double topTemp = baseTemp + lapseRate * deltaH;

                    if (Math.Abs(lapseRate) < 1e-10)
                    {
                        pressure *= Math.Exp(-GravitationalAcceleration * deltaH / (GasConstant * baseTemp));
                    }
                    else
                    {
                        double exponent = -GravitationalAcceleration / (lapseRate * GasConstant);
                        pressure *= Math.Pow(topTemp / baseTemp, exponent);
                    }

                    currentAltitude = layerTop;
                }
            }

            return (temperature, pressure);
        }

        /// <summary>
        /// Converts True Airspeed (TAS) to Indicated Airspeed (IAS).
        /// </summary>
        public static double TasToIas(double tas, double altitude)
        {
            var atm = GetAtmosphereAt(altitude);
            return tas * Math.Sqrt(atm.DensityRatio);
        }

        /// <summary>
        /// Converts Indicated Airspeed (IAS) to True Airspeed (TAS).
        /// </summary>
        public static double IasToTas(double ias, double altitude)
        {
            var atm = GetAtmosphereAt(altitude);
            return ias / Math.Sqrt(atm.DensityRatio);
        }

        /// <summary>
        /// Converts True Airspeed to Mach number.
        /// </summary>
        public static double TasToMach(double tas, double altitude)
        {
            var atm = GetAtmosphereAt(altitude);
            return tas / atm.SpeedOfSound;
        }

        /// <summary>
        /// Converts Mach number to True Airspeed.
        /// </summary>
        public static double MachToTas(double mach, double altitude)
        {
            var atm = GetAtmosphereAt(altitude);
            return mach * atm.SpeedOfSound;
        }

        /// <summary>
        /// Calculates pressure altitude from geometric altitude and local pressure.
        /// </summary>
        public static double GeometricToPressureAltitude(double geometricAltitude, double localPressure = SeaLevelPressure)
        {
            // Simplified calculation assuming standard troposphere
            double pressureRatio = localPressure / SeaLevelPressure;
            double stdPressureAlt = SeaLevelTemperature / 0.0065 * (1 - Math.Pow(pressureRatio, 0.190284));
            return geometricAltitude + stdPressureAlt;
        }

        /// <summary>
        /// Calculates density altitude (critical for performance calculations).
        /// </summary>
        public static double GetDensityAltitude(double pressureAltitude, double outsideAirTemp)
        {
            double stdTemp = SeaLevelTemperature - 0.0065 * pressureAltitude;
            double tempDeviation = outsideAirTemp - stdTemp;
            return pressureAltitude + 120 * tempDeviation; // Rule of thumb: 120ft per 1°C deviation
        }
    }

    /// <summary>
    /// Represents atmospheric conditions at a specific altitude.
    /// </summary>
    public class AtmosphereState
    {
        /// <summary>Geometric altitude in meters</summary>
        public double Altitude { get; init; }
        
        /// <summary>Static air temperature in Kelvin</summary>
        public double Temperature { get; init; }
        
        /// <summary>Static air pressure in Pascals</summary>
        public double Pressure { get; init; }
        
        /// <summary>Air density in kg/m³</summary>
        public double Density { get; init; }
        
        /// <summary>Speed of sound in m/s</summary>
        public double SpeedOfSound { get; init; }
        
        /// <summary>Density ratio (σ = ρ/ρ₀)</summary>
        public double DensityRatio { get; init; }
        
        /// <summary>Pressure ratio (δ = P/P₀)</summary>
        public double PressureRatio { get; init; }
        
        /// <summary>Temperature ratio (θ = T/T₀)</summary>
        public double TemperatureRatio { get; init; }

        /// <summary>Temperature in Celsius</summary>
        public double TemperatureCelsius => Temperature - 273.15;

        /// <summary>Dynamic viscosity in Pa·s (Sutherland's formula)</summary>
        public double DynamicViscosity
        {
            get
            {
                const double mu0 = 1.716e-5;  // Reference viscosity at T0
                const double T0 = 273.15;      // Reference temperature
                const double S = 110.4;        // Sutherland's constant
                return mu0 * Math.Pow(Temperature / T0, 1.5) * (T0 + S) / (Temperature + S);
            }
        }
    }
}
