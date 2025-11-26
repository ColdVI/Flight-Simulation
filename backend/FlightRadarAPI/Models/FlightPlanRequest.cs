using System.ComponentModel.DataAnnotations;

namespace FlightRadarAPI.Models
{
    public class FlightPlanRequest
    {
        [Required]
        [StringLength(16, MinimumLength = 3)]
        public string Callsign { get; set; } = string.Empty;

        [Required]
        [StringLength(16, MinimumLength = 2)]
        public string AircraftTail { get; set; } = string.Empty;

        [Required]
        [StringLength(8, MinimumLength = 2)]
        public string OriginCode { get; set; } = string.Empty;

        [Required]
        [StringLength(8, MinimumLength = 2)]
        public string DestinationCode { get; set; } = string.Empty;

        [Range(10, 400)]
        public double? PlannedSpeedMs { get; set; }

        public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow.AddMinutes(2);
    }
}
