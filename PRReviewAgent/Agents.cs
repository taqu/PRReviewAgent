using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace PRReviewAgent
{
    /// <summary>
    /// Manages different types of AI agents and their interactions with the OpenAI API.
    /// </summary>
    public class Agents
    {
        /// <summary>
        /// Builds and initializes an OpenAI ChatClient and AIAgent based on the provided name and configuration.
        /// </summary>
        /// <param name="chatClient">The initialized ChatClient.</param>
        /// <param name="chatCompletionOptions">The initialized ChatCompletionOptions.</param>
        /// <param name="name">The name identifier used to look up configuration settings.</param>
        private static void Build(out OpenAI.Chat.ChatClient chatClient, out string instructions, ChatCompletionOptions chatCompletionOptions, string name)
        {
            // Retrieve OpenAI API key from secrets
            Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["openai"];
            string apiKey = (string)secrets["api_key"];

            // Load agent-specific configuration settings
            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["agent"];
            string model = (string)config[$"{name}_model"];
            long max_output = (long)config[$"{name}_max_output"];
            if (!model.StartsWith("gpt-5"))
            {
                double temperature = (double)config[$"{name}_temperature"];
                double topp = (double)config[$"{name}_topp"];
                //long topk = (long)config[$"{name}_topk"];
                double frequencyPenalty = (double)config[$"{name}_frequency_penalty"];

                chatCompletionOptions.Temperature = (float)temperature;
                chatCompletionOptions.TopP = (float)topp;
                chatCompletionOptions.FrequencyPenalty = (float)frequencyPenalty;
            }
            long thinkingEffort = Math.Clamp((long)config[$"{name}_thinking_effort"], 0, 3);
            //long thinkingOutput = (long)config[$"{name}_thinking_output"];
            long timeout = (long)config[$"{name}_timeout"];
            instructions = (string)config[$"{name}_instructions"];

            // Configure OpenAI client options including endpoint and timeout
            OpenAIClientOptions options = new OpenAIClientOptions();
            options.Endpoint = new Uri((string)config[$"{name}"]);
            options.NetworkTimeout = TimeSpan.FromSeconds(timeout);

            // Initialize the ChatClient with the API key and options
            chatClient = new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), options);

            chatCompletionOptions.MaxOutputTokenCount = (int)max_output;
#pragma warning disable OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
            switch (thinkingEffort)
            {
                case 0:
                    chatCompletionOptions.ReasoningEffortLevel = ChatReasoningEffortLevel.None;
                    break;
                case 1:
                    chatCompletionOptions.ReasoningEffortLevel = ChatReasoningEffortLevel.Low;
                    break;
                case 2:
                    chatCompletionOptions.ReasoningEffortLevel = ChatReasoningEffortLevel.Medium;
                    break;
                case 3:
                    chatCompletionOptions.ReasoningEffortLevel = ChatReasoningEffortLevel.High;
                    break;
            }
#pragma warning restore OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Agents"/> class, building the default set of agents.
        /// </summary>
        public Agents()
        {
            // Build the three core agents: assistant, planner, and executor
            Build(out chatClient_, out instructions_, chatCompletionOptions_, "reviewer");
        }

        /// <summary>
        /// Runs the specified agent asynchronously with a prompt.
        /// </summary>
        /// <param name="type">The type of agent to run.</param>
        /// <param name="prompt">The prompt to send to the agent.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="AgentResponse"/>.</returns>
        public async Task<string> RunAsync(string prompt, CancellationToken cancellationToken)
        {
            // Execute the agent and return the raw response
            OpenAI.Chat.ChatMessage[] messages;
            if (string.IsNullOrEmpty(instructions_))
            {
                messages = new OpenAI.Chat.ChatMessage[1];
                messages[0] = OpenAI.Chat.ChatMessage.CreateUserMessage(prompt);
            }
            else
            {
                messages = new OpenAI.Chat.ChatMessage[2];
                messages[0] = OpenAI.Chat.ChatMessage.CreateSystemMessage(instructions_);
                messages[1] = OpenAI.Chat.ChatMessage.CreateUserMessage(prompt);
            }
            ClientResult<ChatCompletion> response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions_);
            if (response.Value.Content.Count <= 0)
            {
                return string.Empty;
            }
            return response.Value.Content[0].Text;
        }

        /// <summary>
        /// Runs the specified agent asynchronously with a prompt.
        /// </summary>
        /// <param name="type">The type of agent to run.</param>
        /// <param name="prompt">The prompt to send to the agent.</param>
        /// <param name="reasoningEffort">Reasoning effort</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="AgentResponse"/>.</returns>
#pragma warning disable OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
        public async Task<string> RunAsync(string prompt, ChatReasoningEffortLevel reasoningEffort, CancellationToken cancellationToken)
#pragma warning restore OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
        {
            // Execute the agent and return the raw response
            OpenAI.Chat.ChatMessage[] messages;
            if (string.IsNullOrEmpty(instructions_))
            {
                messages = new OpenAI.Chat.ChatMessage[1];
                messages[0] = OpenAI.Chat.ChatMessage.CreateUserMessage(prompt);
            }
            else
            {
                messages = new OpenAI.Chat.ChatMessage[2];
                messages[0] = OpenAI.Chat.ChatMessage.CreateSystemMessage(instructions_);
                messages[1] = OpenAI.Chat.ChatMessage.CreateUserMessage(prompt);
            }
#pragma warning disable OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
            ChatReasoningEffortLevel? oldReasoningEffort = chatCompletionOptions_.ReasoningEffortLevel;
            chatCompletionOptions_.ReasoningEffortLevel = reasoningEffort;
            ClientResult<ChatCompletion> response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions_, cancellationToken);
            chatCompletionOptions_.ReasoningEffortLevel = oldReasoningEffort;
#pragma warning restore OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします
            if (response.Value.Content.Count <= 0)
            {
                return string.Empty;
            }
            return response.Value.Content[0].Text;
        }

#if false
        /// <summary>
        /// Runs the specified agent asynchronously with a prompt and returns the result deserialized to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the agent's response into.</typeparam>
        /// <param name="type">The type of agent to run.</param>
        /// <param name="prompt">The prompt to send to the agent.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized response of type <typeparamref name="T"/>.</returns>
        public async Task<T> RunAsync<T>(string prompt, CancellationToken cancellationToken)
        {
            // Set the expected response format to JSON schema based on type T
            runOptions_.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(AIJsonUtilities.CreateJsonSchema(typeof(T)));

            // Execute the agent
            AgentResponse response = await agent_.aiAgent_.RunAsync(prompt, null, runOptions_, cancellationToken);

            // Reset response format for subsequent calls
            runOptions_.ResponseFormat = null;

            // Deserialize the JSON response text into type T
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(response.Text);
        }
#endif
        private OpenAI.Chat.ChatClient chatClient_;
        private ChatCompletionOptions chatCompletionOptions_ = new ChatCompletionOptions();
        private string instructions_ = string.Empty;
    }
}
