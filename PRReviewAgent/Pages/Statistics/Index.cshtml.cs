using Microsoft.AspNetCore.Mvc.RazorPages;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Pages.Statistics
{
    public class IndexModel : PageModel
    {
        private readonly IStatisticsService _stats;

        public OverviewStatistics Overview { get; private set; } = new();
        public IReadOnlyList<ReviewTrendPoint> ReviewTrend { get; private set; } = Array.Empty<ReviewTrendPoint>();
        public IReadOnlyList<TokenTrendPoint> TokenTrend { get; private set; } = Array.Empty<TokenTrendPoint>();

        public IndexModel(IStatisticsService stats) => _stats = stats;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            StatisticsQuery query = new(
                ProjectId: null,
                From: DateTimeOffset.UtcNow.AddDays(-30),
                To: DateTimeOffset.UtcNow);
            Overview = await _stats.GetOverviewAsync(query, cancellationToken);
            ReviewTrend = await _stats.GetReviewTrendAsync(query, cancellationToken);
            TokenTrend = await _stats.GetTokenTrendAsync(query, cancellationToken);
        }

        public static string FormatDuration(double? ms)
        {
            if (!ms.HasValue) return "\u2014";
            if (ms.Value < 1000) return $"{ms.Value:F0} ms";
            if (ms.Value < 60000) return $"{ms.Value / 1000.0:F1} s";
            return $"{ms.Value / 60000.0:F1} min";
        }
    }
}
