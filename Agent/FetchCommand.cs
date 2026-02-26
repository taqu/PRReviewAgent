using Spectre.Console.Cli;

namespace Agent
{
    public class FetchCommand : AsyncCommand
    {
        public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
        {
            try
            {
                using var httpClient = new HttpClient();
                // Pass the cancellation token to async operations
                var response = await httpClient.GetStringAsync(settings.Url, cancellationToken);
                System.Console.WriteLine($"Fetched {response.Length} characters");
                return 0;
            }
            catch (OperationCanceledException)
            {
                System.Console.WriteLine("Request was cancelled.");
                return 1;
            }
        }
    }
}
