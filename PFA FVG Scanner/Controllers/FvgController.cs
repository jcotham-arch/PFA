using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class FvgController : ControllerBase
    {
        private readonly FvgTrackingService _tracker;

        public FvgController(FvgTrackingService tracker)
        {
            _tracker = tracker;
        }

        [HttpGet]
        public ActionResult GetAll()
        {
            return Ok(_tracker.GetAll());
        }

        [HttpPost]
        public ActionResult Add([FromBody] FairValueGap gap)
        {
            var tracked = _tracker.Add(gap);

            return Ok(tracked);
        }

        [HttpPut("{gapId:guid}/price")]
        public ActionResult UpdatePrice(
            Guid gapId,
            [FromQuery] decimal price)
        {
            var updated = _tracker.UpdatePrice(
                gapId,
                price);

            if (updated is null)
            {
                return NotFound(new
                {
                    message = "FVG not found."
                });
            }

            return Ok(updated);
        }

        [HttpPut("symbol/{symbol}/price")]
        public ActionResult UpdateSymbolPrice(
            string symbol,
            [FromQuery] decimal price)
        {
            var updated = _tracker.UpdateSymbolPrice(
                symbol,
                price);

            return Ok(new
            {
                symbol,
                currentPrice = price,
                updatedCount = updated.Count,
                gaps = updated
            });
        }

        [HttpDelete("{gapId:guid}")]
        public ActionResult Remove(Guid gapId)
        {
            var removed = _tracker.Remove(gapId);

            if (!removed)
            {
                return NotFound(new
                {
                    message = "FVG not found."
                });
            }

            return Ok(new
            {
                removed = true,
                gapId
            });
        }

        [HttpDelete]
        public ActionResult Clear()
        {
            _tracker.Clear();

            return Ok(new
            {
                cleared = true
            });
        }
    }
}