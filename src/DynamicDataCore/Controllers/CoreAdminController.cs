using Microsoft.AspNetCore.Mvc;

namespace DynamicDataCore.Controllers
{
    [CoreAdminAuth]
    public class CoreAdminController : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
