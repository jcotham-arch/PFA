using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.MarketData;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MassiveDebugController : ControllerBase
    {
        private readonly MassiveMarketDataProvider _provider;

        public MassiveDebugController(
            MassiveMarketDataProvider provider)
        {
            _provider = provider;
        }

        [HttpGet("raw")]
        public ActionResult GetLastRawMessage()
        {
            return Ok(new
            {
                lastRawMessageUtc =
                    _provider.LastRawMessageUtc,

                raw =
                    _provider.LastRawMessage
            });
        }
    }
}