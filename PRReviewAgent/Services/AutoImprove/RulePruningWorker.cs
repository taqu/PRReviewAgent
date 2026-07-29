namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RulePruningWorker : BackgroundService
    {
        private readonly RuleRepository _repository;
        private readonly ILogger<RulePruningWorker> _logger;
        private static readonly TimeSpan Interval = TimeSpan.FromDays(7);

        public RulePruningWorker(RuleRepository repository, ILogger<RulePruningWorker> logger)
        {
            _repository = repository;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(Interval, stoppingToken);
                    await _repository.PruneStaleAsync(stoppingToken);
                    _logger.LogInformation("Pruned stale learned rules.");
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to prune stale rules");
                }
            }
        }
    }
}
