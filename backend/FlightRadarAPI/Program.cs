using FlightRadarAPI.Data;
using FlightRadarAPI.Services;
using Microsoft.Extensions.FileProviders;
using System.IO;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// Bind to all interfaces so Docker publishes the port correctly, respecting overrides from --urls / env vars
var configuredUrls = builder.Configuration["urls"] ?? builder.Configuration["ASPNETCORE_URLS"];
if (string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls("http://0.0.0.0:5001");
}
else
{
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Services.AddControllers().AddJsonOptions(opts =>
{
    opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

builder.Services.AddSingleton<IFlightRepository, FlightRepository>();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationService>());

// CORS İzni (Frontend'in Backend'e erişmesi için şart)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseCors("AllowAll");

// Frontend dizinini statik dosya olarak sun
var frontendCandidates = new[]
{
    Path.Combine(builder.Environment.ContentRootPath, "..", "..", "frontend"),
    Path.Combine(builder.Environment.ContentRootPath, "frontend"),
    Path.Combine(AppContext.BaseDirectory, "frontend")
};

var existingFrontendPath = frontendCandidates.FirstOrDefault(Directory.Exists);
if (existingFrontendPath is not null)
{
    var physical = new PhysicalFileProvider(Path.GetFullPath(existingFrontendPath));
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = physical });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = physical });
}

app.MapControllers();

app.Run();