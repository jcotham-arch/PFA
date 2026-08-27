using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class CandleController : ControllerBase
    {
        private readonly CandleProcessingService _processor;
        private readonly FvgTrackingService _tracker;

        public CandleController(
            CandleProcessingService processor,
            FvgTrackingService tracker)
        {
            _processor = processor;
            _tracker = tracker;
        }

        [HttpPost]
        public ActionResult ProcessCandle(
            [FromBody] Candle candle)
        {
            CandleProcessingResult result =
                _processor.ProcessClosedCandle(candle);

            if (!result.Accepted)
            {
                return BadRequest(result);
            }

            return Ok(result);
        }

        [HttpGet("{symbol}/{timeframe}")]
        public ActionResult GetWindow(
            string symbol,
            string timeframe)
        {
            var candles =
                _processor.GetCurrentWindow(
                    symbol,
                    timeframe);

            return Ok(new
            {
                symbol,
                timeframe,
                count = candles.Count,
                candles
            });
        }

        [HttpDelete]
        public ActionResult Clear()
        {
            _processor.Clear();
            _tracker.Clear();

            return Ok(new
            {
                cleared = true
            });
        }
    }
}