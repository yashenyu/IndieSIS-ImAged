using Microsoft.AspNetCore.Mvc;

namespace ImagedBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LogController : ControllerBase
    {
        [HttpPost]
        public IActionResult LogEvent([FromBody] LogEvent log)
        {
            Console.WriteLine($"Event: {log.Event}, Detail: {log.Detail}");
            return Ok(new { message = "Log received" });
        }
    }

    public class LogEvent
    {
        public string Event { get; set; } = "";
        public string Detail { get; set; } = "";
    }
}


