using PFA_FVG_Scanner.Models;

namespace PFA_FVG_Scanner.Services
{
    public sealed class FvgTrackingService
    {
        private readonly List<FairValueGap> _trackedGaps = new();

        public IReadOnlyList<FairValueGap> GetAll()
        {
            return _trackedGaps
                .OrderByDescending(x => x.FormationTimeUtc)
                .ToList();
        }

        public FairValueGap Add(FairValueGap gap)
        {
            var existing = _trackedGaps.FirstOrDefault(x =>
                x.Symbol.Equals(
                    gap.Symbol,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Timeframe.Equals(
                    gap.Timeframe,
                    StringComparison.OrdinalIgnoreCase) &&
                x.Direction == gap.Direction &&
                x.FormationTimeUtc == gap.FormationTimeUtc &&
                x.LowerBoundary == gap.LowerBoundary &&
                x.UpperBoundary == gap.UpperBoundary);

            if (existing is not null)
            {
                return existing;
            }

            gap.Status = FvgStatus.Active;
            gap.FillPercentage = 0m;

            _trackedGaps.Add(gap);

            return gap;
        }

        public FairValueGap? UpdatePrice(
            Guid gapId,
            decimal currentPrice)
        {
            var gap = _trackedGaps.FirstOrDefault(x => x.Id == gapId);

            if (gap is null)
            {
                return null;
            }

            gap.CurrentPrice = currentPrice;

            UpdateGapState(gap, currentPrice);

            return gap;
        }

        public IReadOnlyList<FairValueGap> UpdateSymbolPrice(
            string symbol,
            decimal currentPrice)
        {
            var matchingGaps = _trackedGaps
                .Where(x =>
                    x.Symbol.Equals(
                        symbol,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Status != FvgStatus.FullyFilled &&
                    x.Status != FvgStatus.Invalidated)
                .ToList();

            foreach (var gap in matchingGaps)
            {
                gap.CurrentPrice = currentPrice;
                UpdateGapState(gap, currentPrice);
            }

            return matchingGaps;
        }

        public bool Remove(Guid gapId)
        {
            var gap = _trackedGaps.FirstOrDefault(x => x.Id == gapId);

            if (gap is null)
            {
                return false;
            }

            return _trackedGaps.Remove(gap);
        }

        public void Clear()
        {
            _trackedGaps.Clear();
        }

        private static void UpdateGapState(
            FairValueGap gap,
            decimal currentPrice)
        {
            if (gap.Direction == FvgDirection.Bullish)
            {
                UpdateBullishGap(gap, currentPrice);
                return;
            }

            UpdateBearishGap(gap, currentPrice);
        }

        private static void UpdateBullishGap(
            FairValueGap gap,
            decimal currentPrice)
        {
            decimal upper = gap.UpperBoundary;
            decimal lower = gap.LowerBoundary;
            decimal midpoint = gap.Midpoint;

            if (currentPrice >= upper)
            {
                gap.FillPercentage = 0m;
                gap.Status = FvgStatus.Active;
                return;
            }

            if (currentPrice <= lower)
            {
                gap.FillPercentage = 100m;
                gap.Status = FvgStatus.FullyFilled;
                return;
            }

            decimal totalGap = upper - lower;
            decimal amountFilled = upper - currentPrice;

            gap.FillPercentage =
                Math.Clamp(
                    (amountFilled / totalGap) * 100m,
                    0m,
                    100m);

            if (currentPrice <= midpoint)
            {
                gap.Status = FvgStatus.FiftyPercentFilled;
            }
            else
            {
                gap.Status = FvgStatus.PartiallyFilled;
            }
        }

        private static void UpdateBearishGap(
            FairValueGap gap,
            decimal currentPrice)
        {
            decimal upper = gap.UpperBoundary;
            decimal lower = gap.LowerBoundary;
            decimal midpoint = gap.Midpoint;

            if (currentPrice <= lower)
            {
                gap.FillPercentage = 0m;
                gap.Status = FvgStatus.Active;
                return;
            }

            if (currentPrice >= upper)
            {
                gap.FillPercentage = 100m;
                gap.Status = FvgStatus.FullyFilled;
                return;
            }

            decimal totalGap = upper - lower;
            decimal amountFilled = currentPrice - lower;

            gap.FillPercentage =
                Math.Clamp(
                    (amountFilled / totalGap) * 100m,
                    0m,
                    100m);

            if (currentPrice >= midpoint)
            {
                gap.Status = FvgStatus.FiftyPercentFilled;
            }
            else
            {
                gap.Status = FvgStatus.PartiallyFilled;
            }
        }
    }
}