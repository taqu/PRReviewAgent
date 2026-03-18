namespace PRReviewAgent.Services
{
    public class WarmUpTask : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            await Context.Instance.WarmUpAsync();
        }
    }
}

