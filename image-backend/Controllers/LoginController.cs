using Microsoft.AspNetCore.Mvc;
using System.Collections.Concurrent;

namespace ImagedBackend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoginController : ControllerBase
    {
        // in-memory store for demo only
        private static readonly ConcurrentDictionary<string, string> Sessions = new();

        [HttpPost]
        public IActionResult Login([FromBody] LoginRequest req)
        {
            if (req is null) return BadRequest();

            // Demo credentials (change as needed)
            if (req.Username == "admin" && req.Password == "1234")
            {
                var token = Guid.NewGuid().ToString();
                Sessions[token] = req.Username;

                // Try to write token to host-mounted folder /sessions/session.txt
                try
                {
                    System.IO.Directory.CreateDirectory("/sessions");
                    System.IO.File.WriteAllText("/sessions/session.txt", token);
                }
                catch { /* ignore IO errors for demo */ }

                return Ok(new { token });
            }

            return Unauthorized(new { message = "Invalid credentials" });
        }

        // verify token: GET /api/login/verify?token=...
        [HttpGet("verify")]
        public IActionResult Verify([FromQuery] string token)
        {
            if (!string.IsNullOrWhiteSpace(token) && Sessions.ContainsKey(token))
                return Ok(new { valid = true });
            return Unauthorized(new { valid = false });
        }

        // optional: clear server-side session & session file
        [HttpPost("logout")]
        public IActionResult Logout([FromBody] LogoutRequest req)
        {
            if (!string.IsNullOrWhiteSpace(req?.Token))
            {
                Sessions.TryRemove(req.Token, out _);
                try { System.IO.File.WriteAllText("/sessions/session.txt", ""); } catch { }
            }
            return Ok();
        }

        public record LoginRequest(string Username, string Password);
        public record LogoutRequest(string Token);
    }
}
