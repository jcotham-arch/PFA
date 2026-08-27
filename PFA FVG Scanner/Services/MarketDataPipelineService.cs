using PFA_FVG_Scanner.Data;
using PFA_FVG_Scanner.MarketData;
using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class MarketDataPipelineService
    {
        private readonly IMarketDataProvider _provider;
        private readonly CandleProcessingService _processor;
        private readonly CandleRepository _candleRepository;
        private readonly ObservationRepository _observationRepository;

        private bool _initialized;

        public CandleProcessingResult? LastProcessingResult
        {
            get;
            private set;
        }

        public MarketDataPipelineService(
            IMarketDataProvider provider,
            CandleProcessingService processor,
            CandleRepository candleRepository,
            ObservationRepository observationRepository)
        {
            _provider = provider;
            _processor = processor;
            _candleRepository = candleRepository;
            _observationRepository = observationRepository;
        }

        public IMarketDataProvider Provider =>
            _provider;

        public void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            _provider.ClosedCandleReceived +=
                HandleClosedCandleAsync;

            _initialized = true;
        }

        private async Task HandleClosedCandleAsync(
            Candle candle)
        {
            await _candleRepository.SaveAsync(
                candle,
                _provider.ProviderName);

            LastProcessingResult =
                _processor.ProcessClosedCandle(candle);

            if (LastProcessingResult.DetectedFvg is not null)
            {
                _observationRepository.SaveFvg(
                    LastProcessingResult.DetectedFvg);
            }
        }
    }
}