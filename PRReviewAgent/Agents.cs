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
    public class Agents
    {
        public enum Type
        {
            Assistant = 0,
            Planner = 1,
            Executor = 2,
        }
        private static void Build(out OpenAI.Chat.ChatClient chatClient, out AIAgent aIAgent, string name)
        {
            Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["openai"];
            string apiKey = (string)secrets["api_key"];

            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["agent"];
            string model = (string)config[$"{name}_model"];
            double temperature = (double)config[$"{name}_temperature"];
            long thinkingEffort = (long)config[$"{name}_thinking_effort"];
            long thinkingOutput = (long)config[$"{name}_thinking_output"];
            long timeout = (long)config[$"{name}_timeout"];

            OpenAIClientOptions options = new OpenAIClientOptions();
            options.Endpoint = new Uri((string)config[$"{name}"]);
            options.NetworkTimeout = TimeSpan.FromSeconds(timeout);
            chatClient = new OpenAI.Chat.ChatClient(model, new ApiKeyCredential(apiKey), options);
            aIAgent = chatClient.AsAIAgent(
                options: new ChatClientAgentOptions()
                {
                    Name = (string)config[$"{name}_name"],
                    ChatOptions = new()
                    {
                        Temperature = (float)temperature,
                        Instructions = (string)config[$"{name}_instructions"],
                        Reasoning = new ReasoningOptions
                        {
                            Effort = (ReasoningEffort)Math.Clamp(thinkingEffort, 0, 3),
                            Output = (ReasoningOutput)Math.Clamp(thinkingOutput, 0, 2),
                        }
                    },
                }
            );
        }

        public Agents()
        {
            Build(out agents_[0].chatClient_, out agents_[0].aiAgent_, "assistant");
            Build(out agents_[1].chatClient_, out agents_[1].aiAgent_, "planner");
            Build(out agents_[2].chatClient_, out agents_[2].aiAgent_, "executor");
        }

        public async Task<AgentResponse> RunAsync(Type type, string prompt, CancellationToken cancellationToken)
        {
            try
            {
                AgentResponse response = await agents_[(int)type].aiAgent_.RunAsync(prompt, session_, runOptions_, cancellationToken);
                return response;
            }
            catch(Exception e)
            {
                System.Console.WriteLine(e.ToString()); 
                return new AgentResponse();
            }
        }

        public async Task<T> RunAsync<T>(Type type, string prompt, CancellationToken cancellationToken)
        {
            try
            {
                runOptions_.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(AIJsonUtilities.CreateJsonSchema(typeof(T)));
                AgentResponse response = await agents_[(int)type].aiAgent_.RunAsync(prompt, session_, runOptions_, cancellationToken);
                runOptions_.ResponseFormat = null;
                return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(response.Text);
            }
            catch
            {
                return default;
            }
        }

        private struct Agent
        {
            public OpenAI.Chat.ChatClient chatClient_;
            public AIAgent aiAgent_;
        }
        private Agent[] agents_ = new Agent[3];
        private AgentRunOptions runOptions_ = new AgentRunOptions();
        private AgentSession? session_;
    }
}
