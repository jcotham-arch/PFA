using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class FvgTestController : ControllerBase
    {
        private readonly FvgDetectionService _detector;

        public FvgTestController(FvgDetectionService detector)
        {
            _detector = detector;
        }

        [HttpGet("bullish")]
        public ActionResult GetBullishTest()
        {
            var candle1 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-10),
                Open = 7680.00m,
                High = 7683.50m,
                Low = 7679.25m,
                Close = 7682.75m,
                Volume = 1250m,
                IsClosed = true
            };

            var candle2 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-5),
                Open = 7682.75m,
                High = 7687.25m,
                Low = 7682.50m,
                Close = 7686.50m,
                Volume = 1840m,
                IsClosed = true
            };

            var candle3 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow,
                Open = 7686.50m,
                High = 7689.00m,
                Low = 7685.00m,
                Close = 7688.25m,
                Volume = 1625m,
                IsClosed = true
            };

            return BuildResponse(candle1, candle2, candle3);
        }

        [HttpGet("bearish")]
        public ActionResult GetBearishTest()
        {
            var candle1 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-10),
                Open = 7690.00m,
                High = 7691.25m,
                Low = 7687.50m,
                Close = 7688.00m,
                Volume = 1320m,
                IsClosed = true
            };

            var candle2 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-5),
                Open = 7688.00m,
                High = 7688.50m,
                Low = 7683.25m,
                Close = 7684.00m,
                Volume = 1900m,
                IsClosed = true
            };

            var candle3 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow,
                Open = 7684.00m,
                High = 7686.50m,
                Low = 7681.75m,
                Close = 7682.50m,
                Volume = 1710m,
                IsClosed = true
            };

            return BuildResponse(candle1, candle2, candle3);
        }

        [HttpGet("none")]
        public ActionResult GetNoFvgTest()
        {
            var candle1 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-10),
                Open = 7680.00m,
                High = 7685.00m,
                Low = 7678.00m,
                Close = 7683.00m,
                Volume = 1100m,
                IsClosed = true
            };

            var candle2 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow.AddMinutes(-5),
                Open = 7683.00m,
                High = 7686.00m,
                Low = 7681.00m,
                Close = 7684.00m,
                Volume = 1250m,
                IsClosed = true
            };

            var candle3 = new Candle
            {
                Symbol = "MES",
                Timeframe = "5m",
                OpenTimeUtc = DateTime.UtcNow,
                Open = 7684.00m,
                High = 7687.00m,
                Low = 7684.50m,
                Close = 7686.00m,
                Volume = 1180m,
                IsClosed = true
            };

            return BuildResponse(candle1, candle2, candle3);
        }

        private ActionResult BuildResponse(
            Candle candle1,
            Candle candle2,
            Candle candle3)
        {
            var result = _detector.Detect(
                candle1,
                candle2,
                candle3);

            if (result is null)
            {
                return Ok(new
                {
                    detected = false,
                    message = "No FVG detected."
                });
            }

            return Ok(new
            {
                detected = true,
                result.Direction,
                result.Symbol,
                result.Timeframe,
                result.LowerBoundary,
                result.UpperBoundary,
                result.GapSize,
                result.Midpoint,
                result.FormationTimeUtc,
                result.Status
            });
        }
    }
}