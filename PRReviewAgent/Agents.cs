using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Octokit.Webhooks.Models.PageBuildEvent;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;

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
        /// <param name="aIAgent">The initialized AIAgent.</param>
        /// <param name="name">The name identifier used to look up configuration settings.</param>
        private static void Build(out OpenAI.Chat.ChatClient chatClient, out AIAgent aIAgent, string name)
        {
            // Retrieve OpenAI API key from secrets
            Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["openai"];
            string apiKey = (string)secrets["api_key"];

            // Load agent-specific configuration settings
            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["agent"];
            string model = (string)config[$"{name}_model"];
            long max_output = (long)config[$"{name}_max_output"];
            double temperature = (double)config[$"{name}_temperature"];
            double topp = (double)config[$"{name}_topp"];
            long topk = (long)config[$"{name}_topk"];
            long thinkingEffort = (long)config[$"{name}_thinking_effort"];
            long thinkingOutput = (long)config[$"{name}_thinking_output"];
            long timeout = (long)config[$"{name}_timeout"];

            // Configure OpenAI client options including endpoint and timeout
            OpenAIClientOptions options = new OpenAIClientOptions();
            options.Endpoint = new Uri((string)config[$"{name}"]);
            options.NetworkTimeout = TimeSpan.FromSeconds(timeout);

            // Initialize the ChatClient with the API key and options
            chatClient = new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), options);

            // Create the AIAgent using the ChatClient and specified options
            aIAgent = chatClient.AsAIAgent(
                options: new ChatClientAgentOptions()
                {
                    Name = (string)config[$"{name}_name"],
                    ChatOptions = new()
                    {
                        MaxOutputTokens = (int)max_output,
                        Temperature = (float)temperature,
                        TopP = (float)topp,
                        TopK = (int)topk,
                        Instructions = (string)config[$"{name}_instructions"],
                        Reasoning = new ReasoningOptions
                        {
                            Effort = (ReasoningEffort)Math.Clamp(thinkingEffort, 0, 3),
                            Output = (ReasoningOutput)Math.Clamp(thinkingOutput, 0, 2),
                        }
                    },
                    ChatHistoryProvider = null,
                }
            );
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Agents"/> class, building the default set of agents.
        /// </summary>
        public Agents()
        {
            // Build the three core agents: assistant, planner, and executor
            Build(out agent_.chatClient_, out agent_.aiAgent_, "reviewer");
        }

        /// <summary>
        /// Runs the specified agent asynchronously with a prompt.
        /// </summary>
        /// <param name="type">The type of agent to run.</param>
        /// <param name="prompt">The prompt to send to the agent.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the <see cref="AgentResponse"/>.</returns>
        public async Task<AgentResponse> RunAsync(string prompt, CancellationToken cancellationToken)
        {
            // Execute the agent and return the raw response
            AgentResponse response = await agent_.aiAgent_.RunAsync(prompt, null, runOptions_, cancellationToken);
            return response;
        }

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

        /// <summary>
        /// Represents an individual AI agent consisting of a ChatClient and an AIAgent.
        /// </summary>
        private struct Agent
        {
            public OpenAI.Chat.ChatClient chatClient_;
            public AIAgent aiAgent_;
        }
        private Agent agent_ = new Agent();
        private AgentRunOptions runOptions_ = new AgentRunOptions();
    }
}
