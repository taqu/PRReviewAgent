using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace PRReviewAgent.Services.AutoImprove
{
    public sealed class RuleExtractionSubAgent : ISubAgent
    {
        private readonly bool _enabled;
        private readonly OpenAI.Chat.ChatClient? _chatClient;
        private readonly ChatCompletionOptions _options;
        private readonly ILogger<RuleExtractionSubAgent> _logger;

        public RuleExtractionSubAgent(RuleExtractionSubAgentSettings settings, ILogger<RuleExtractionSubAgent> logger)
        {
            _enabled = settings.Enabled;
            _logger = logger;
            _options = new ChatCompletionOptions
            {
                MaxOutputTokenCount = settings.MaxOutput,
                Temperature = (float)settings.Temperature,
                TopP = (float)settings.TopP,
            };

            if (!_enabled) return;
            if (string.IsNullOrWhiteSpace(settings.Endpoint)) return;

            Tomlyn.Model.TomlTable secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["openai"];
            string apiKey = (string)secrets["api_key"];

            OpenAIClientOptions clientOptions = new OpenAIClientOptions();
            clientOptions.Endpoint = new Uri(settings.Endpoint);
            clientOptions.NetworkTimeout = TimeSpan.FromSeconds(settings.TimeoutSeconds);

            _chatClient = new OpenAI.Chat.ChatClient(settings.Model, new ApiKeyCredential(apiKey), clientOptions);
            _logger.LogInformation("Rule extraction SubAgent initialized with name {Name}", settings.Name);
        }

        public async Task<string?> RunAsync(string prompt, CancellationToken cancellationToken = default)
        {
            if (!_enabled)
            {
                _logger.LogDebug("Rule extraction SubAgent is disabled, skipping");
                return null;
            }
            if (_chatClient == null)
            {
                _logger.LogWarning("Rule extraction SubAgent has no valid endpoint, skipping");
                return null;
            }

            try
            {
                _logger.LogInformation("Rule extraction using SubAgent");
                ChatMessage[] messages = [ChatMessage.CreateUserMessage(prompt)];
                ClientResult<ChatCompletion> response = await _chatClient.CompleteChatAsync(messages, _options, cancellationToken);
                if (response.Value.Content.Count <= 0) return null;
                return response.Value.Content[0].Text;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Rule extraction SubAgent failed");
                return null;
            }
        }
    }
}
