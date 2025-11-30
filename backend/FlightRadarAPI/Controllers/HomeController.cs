using Microsoft.AspNetCore.Mvc;

namespace FlightRadarAPI.Controllers;

public class HomeController : Controller
{
    public IActionResult Index()
    {
        return View();
    }
}
