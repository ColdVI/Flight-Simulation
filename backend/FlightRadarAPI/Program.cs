using FlightRadarAPI.Data;
using FlightRadarAPI.Services;
using Microsoft.Extensions.FileProviders;
using Microsoft.AspNetCore.StaticFiles;
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

builder.Services
    .AddControllersWithViews()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });

builder.Services.AddSingleton<IFlightRepository, InMemoryFlightRepository>();
builder.Services.AddSingleton<SimulationService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SimulationService>());

// CORS İzni (Frontend'in Backend'e erişmesi için şart)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", b => b.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

var app = builder.Build();

app.UseStaticFiles();

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
    existingFrontendPath = Path.GetFullPath(existingFrontendPath);
    var rootProvider = new PhysicalFileProvider(existingFrontendPath);
    var contentTypeProvider = new FileExtensionContentTypeProvider();
    contentTypeProvider.Mappings[".czml"] = "application/json";
    contentTypeProvider.Mappings[".geojson"] = "application/json";
    contentTypeProvider.Mappings[".topojson"] = "application/json";
    contentTypeProvider.Mappings[".b3dm"] = "application/octet-stream";
    contentTypeProvider.Mappings[".pnts"] = "application/octet-stream";
    contentTypeProvider.Mappings[".i3dm"] = "application/octet-stream";
    contentTypeProvider.Mappings[".cmpt"] = "application/octet-stream";
    contentTypeProvider.Mappings[".glb"] = "model/gltf-binary";
    contentTypeProvider.Mappings[".gltf"] = "model/gltf+json";
    contentTypeProvider.Mappings[".ktx2"] = "image/ktx2";
    contentTypeProvider.Mappings[".terrain"] = "application/vnd.quantized-mesh";

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = rootProvider,
        ContentTypeProvider = contentTypeProvider
    });

    var cesiumPath = Path.Combine(existingFrontendPath, "public", "cesium");
    if (Directory.Exists(cesiumPath))
    {
        var cesiumProvider = new PhysicalFileProvider(cesiumPath);
        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = cesiumProvider,
            RequestPath = "/cesium",
            ContentTypeProvider = contentTypeProvider
        });
    }
}

app.UseRouting();

app.UseCors("AllowAll");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapControllers();

app.MapFallbackToController("Index", "Home");

app.Run();