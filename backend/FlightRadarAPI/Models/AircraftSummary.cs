namespace FlightRadarAPI.Models
{
    public class AircraftSummary
    {
        public string TailNumber { get; set; } = string.Empty;
        public string Model { get; set; } = string.Empty;
        public string? Manufacturer { get; set; }
        public double CruiseSpeedMs { get; set; }
        public bool IsAvailable { get; set; }
    }
}
