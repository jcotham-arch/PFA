using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Models;
using PFA_FVG_Scanner.Services;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class MarketDataController : ControllerBase
    {
        private readonly MarketDataPipelineService _pipeline;

        public MarketDataController(
            MarketDataPipelineService pipeline)
        {
            _pipeline = pipeline;
            _pipeline.Initialize();
        }

        [HttpGet("status")]
        public ActionResult GetStatus()
        {
            return Ok(
                _pipeline.Provider.ConnectionState);
        }

        [HttpPost("connect")]
        public async Task<ActionResult> Connect()
        {
            await _pipeline.Provider.ConnectAsync();

            return Ok(
                _pipeline.Provider.ConnectionState);
        }

        [HttpPost("disconnect")]
        public async Task<ActionResult> Disconnect()
        {
            await _pipeline.Provider.DisconnectAsync();

            return Ok(
                _pipeline.Provider.ConnectionState);
        }

        [HttpPost("subscribe")]
        public async Task<ActionResult> Subscribe(
            [FromQuery] string symbol = "MES",
            [FromQuery] string timeframe = "5m")
        {
            await _pipeline.Provider.SubscribeAsync(
                symbol,
                timeframe);

            return Ok(
                _pipeline.Provider.ConnectionState);
        }

        [HttpPost("simulate-candle")]
        public async Task<ActionResult> SimulateCandle(
            [FromBody] Candle candle)
        {
            if (_pipeline.Provider is not
                SimulatedMarketDataProvider simulator)
            {
                return BadRequest(new
                {
                    message =
                        "Current provider does not support simulated candles."
                });
            }

            try
            {
                await simulator.PublishClosedCandleAsync(
                    candle);

                return Ok(new
                {
                    provider =
                        simulator.ProviderName,

                    connection =
                        simulator.ConnectionState,

                    processingResult =
                        _pipeline.LastProcessingResult
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new
                {
                    message = ex.Message
                });
            }
        }
    }
}