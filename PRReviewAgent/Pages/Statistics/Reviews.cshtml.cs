using Microsoft.AspNetCore.Mvc.RazorPages;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Pages.Statistics
{
    public class ReviewsModel : PageModel
    {
        private readonly IStatisticsService _stats;

        public ReviewStatistics Reviews { get; private set; } = new();
        public IReadOnlyList<ModelUsageStatistics> ModelUsage { get; private set; } = Array.Empty<ModelUsageStatistics>();

        public ReviewsModel(IStatisticsService stats) => _stats = stats;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            StatisticsQuery query = new(
                ProjectId: null,
                From: DateTimeOffset.UtcNow.AddDays(-30),
                To: DateTimeOffset.UtcNow);
            Reviews = await _stats.GetReviewStatisticsAsync(query, cancellationToken);
            ModelUsage = await _stats.GetModelUsageAsync(query, cancellationToken);
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
