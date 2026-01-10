using FlightRadarAPI.Models;
using FlightRadarAPI.Services;
using Microsoft.AspNetCore.Mvc;

namespace FlightRadarAPI.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ReportsController : ControllerBase
    {
        private readonly FlightDataRecorder _recorder;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(FlightDataRecorder recorder, ILogger<ReportsController> logger)
        {
            _recorder = recorder;
            _logger = logger;
        }

        /// <summary>
        /// Gets all completed flight reports.
        /// </summary>
        [HttpGet]
        public ActionResult<IEnumerable<FlightReportSummary>> GetAllReports()
        {
            var reports = _recorder.GetAllReports();
            var summaries = reports.Select(r => new FlightReportSummary
            {
                Callsign = r.Callsign,
                AircraftModel = r.AircraftModel,
                Route = $"{r.OriginCode} → {r.DestinationCode}",
                DepartureTime = r.DepartureTimeUtc,
                ArrivalTime = r.ArrivalTimeUtc,
                Duration = r.FlightDurationFormatted,
                DistanceNm = Math.Round(r.GreatCircleDistanceNm, 1),
                MaxAltitudeFt = Math.Round(r.MaxAltitudeFeet, 0),
                FuelConsumedKg = Math.Round(r.TotalFuelConsumed, 0)
            });

            return Ok(summaries);
        }

        /// <summary>
        /// Gets a specific flight report.
        /// </summary>
        [HttpGet("{callsign}")]
        public ActionResult<FlightReport> GetReport(string callsign)
        {
            var report = _recorder.GetReport(callsign);
            if (report == null)
            {
                return NotFound(new { error = $"No report found for flight {callsign}" });
            }

            // Return report without telemetry data for performance
            report.TelemetryData = null;
            return Ok(report);
        }

        /// <summary>
        /// Gets the full report including telemetry data (can be large).
        /// </summary>
        [HttpGet("{callsign}/full")]
        public ActionResult<FlightReport> GetFullReport(string callsign)
        {
            var report = _recorder.GetReport(callsign);
            if (report == null)
            {
                return NotFound(new { error = $"No report found for flight {callsign}" });
            }

            return Ok(report);
        }

        /// <summary>
        /// Exports the report as JSON.
        /// </summary>
        [HttpGet("{callsign}/json")]
        public ActionResult ExportJson(string callsign, [FromQuery] bool includeTelemetry = false)
        {
            var json = _recorder.ExportReportJson(callsign, includeTelemetry);
            if (json == null)
            {
                return NotFound(new { error = $"No report found for flight {callsign}" });
            }

            return Content(json, "application/json");
        }

        /// <summary>
        /// Exports telemetry data as CSV.
        /// </summary>
        [HttpGet("{callsign}/csv")]
        public ActionResult ExportCsv(string callsign)
        {
            var csv = _recorder.ExportTelemetryCsv(callsign);
            if (csv == null || string.IsNullOrEmpty(csv))
            {
                return NotFound(new { error = $"No telemetry data found for flight {callsign}" });
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
            return File(bytes, "text/csv", $"{callsign}_telemetry.csv");
        }

        /// <summary>
        /// Gets the recording status for an active flight.
        /// </summary>
        [HttpGet("{callsign}/status")]
        public ActionResult GetRecordingStatus(string callsign)
        {
            var status = _recorder.GetRecordingStatus(callsign);
            if (status == null)
            {
                // Check if there's a completed report
                var report = _recorder.GetReport(callsign);
                if (report != null)
                {
                    return Ok(new
                    {
                        status = "completed",
                        sampleCount = report.TotalSamples,
                        duration = report.FlightDurationFormatted
                    });
                }

                return NotFound(new { status = "not_found" });
            }

            return Ok(new
            {
                status = "recording",
                sampleCount = status.Value.SampleCount,
                duration = status.Value.Duration.ToString(@"hh\:mm\:ss")
            });
        }

        /// <summary>
        /// Deletes a specific flight report.
        /// </summary>
        [HttpDelete("{callsign}")]
        public ActionResult DeleteReport(string callsign)
        {
            if (_recorder.ClearReport(callsign))
            {
                _logger.LogInformation("Deleted report for flight {Callsign}", callsign);
                return Ok(new { message = $"Report for {callsign} deleted" });
            }

            return NotFound(new { error = $"No report found for flight {callsign}" });
        }

        /// <summary>
        /// Deletes all flight reports.
        /// </summary>
        [HttpDelete]
        public ActionResult DeleteAllReports()
        {
            _recorder.ClearAllReports();
            return Ok(new { message = "All reports deleted" });
        }
    }

    /// <summary>
    /// Lightweight summary for listing reports.
    /// </summary>
    public class FlightReportSummary
    {
        public string Callsign { get; set; } = string.Empty;
        public string AircraftModel { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public string Duration { get; set; } = string.Empty;
        public double DistanceNm { get; set; }
        public double MaxAltitudeFt { get; set; }
        public double FuelConsumedKg { get; set; }
    }
}
