using Microsoft.AspNetCore.Mvc.RazorPages;
using PRReviewAgent.Services.Statistics;

namespace PRReviewAgent.Pages.Statistics
{
    public class RulesModel : PageModel
    {
        private readonly IStatisticsService _stats;

        public RuleStatistics Rules { get; private set; } = new();
        public IReadOnlyList<RuleUsageStatistics> RuleUsage { get; private set; } = Array.Empty<RuleUsageStatistics>();

        public RulesModel(IStatisticsService stats) => _stats = stats;

        public async Task OnGetAsync(CancellationToken cancellationToken)
        {
            StatisticsQuery query = new(
                ProjectId: null,
                From: DateTimeOffset.UtcNow.AddDays(-30),
                To: DateTimeOffset.UtcNow);
            Rules = await _stats.GetRuleStatisticsAsync(query, cancellationToken);
            RuleUsage = await _stats.GetRuleUsageAsync(query, cancellationToken);
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
