using Microsoft.AspNetCore.Mvc;
using PFA_FVG_Scanner.Data;

namespace PFA_FVG_Scanner.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public sealed class DatabaseController : ControllerBase
    {
        private readonly CandleRepository _candleRepository;
        private readonly RawMarketEventRepository _rawMarketEventRepository;
        private readonly PfaDatabase _database;

        public DatabaseController(
            CandleRepository candleRepository,
            RawMarketEventRepository rawMarketEventRepository,
            PfaDatabase database)
        {
            _candleRepository = candleRepository;
            _rawMarketEventRepository = rawMarketEventRepository;
            _database = database;
        }

        [HttpGet("info")]
        public ActionResult GetDatabaseInfo()
        {
            return Ok(new
            {
                databasePath = _database.DatabasePath,
                exists = System.IO.File.Exists(
                    _database.DatabasePath)
            });
        }

        [HttpGet("candles/{symbol}/{timeframe}")]
        public async Task<ActionResult> GetRecentCandles(
            string symbol,
            string timeframe,
            [FromQuery] int limit = 100,
            CancellationToken cancellationToken = default)
        {
            var candles =
                await _candleRepository.GetRecentAsync(
                    symbol,
                    timeframe,
                    limit,
                    cancellationToken);

            return Ok(new
            {
                symbol,
                timeframe,
                count = candles.Count,
                candles
            });
        }

        [HttpGet("raw/count")]
        public async Task<ActionResult> GetRawEventCount(
            CancellationToken cancellationToken = default)
        {
            int count =
                await _rawMarketEventRepository.GetCountAsync(
                    cancellationToken);

            return Ok(new
            {
                rawMarketEventCount = count
            });
        }
    }
}