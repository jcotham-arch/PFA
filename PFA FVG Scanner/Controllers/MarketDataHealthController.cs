using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MarketDataHealthController : ControllerBase
    {
        private readonly MarketDataWatchdogService _watchdog;

        public MarketDataHealthController(
            MarketDataWatchdogService watchdog)
        {
            _watchdog = watchdog;
        }

        [HttpGet]
        public ActionResult GetHealth()
        {
            return Ok(new
            {
                isFeedHealthy =
                    _watchdog.IsFeedHealthy,

                isFeedStale =
                    _watchdog.IsFeedStale,

                healthMessage =
                    _watchdog.HealthMessage,

                lastHealthCheckUtc =
                    _watchdog.LastHealthCheckUtc,

                lastReconnectAttemptUtc =
                    _watchdog.LastReconnectAttemptUtc
            });
        }
    }
}