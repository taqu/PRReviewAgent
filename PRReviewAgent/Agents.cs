using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System;
using System.ClientModel;
using System.Collections.Generic;

namespace PRReviewAgent
{
   public class Agents
    {
        public Agents()
        {
            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Settings.Instance.Config["agent"];
            {
                double temperature = (double)config["planner_temperature"];
                long thinkingEffort = (long)config["planner_thinking_effort"];
                long plannerThinkingOutput = (long)config["planner_thinking_output"];
                long plannerTimeout = (long)config["planner_timeout"];

                OpenAIClientOptions options = new OpenAIClientOptions();
                options.Endpoint = new Uri((string)config["planner"]);
                options.NetworkTimeout = TimeSpan.FromSeconds(plannerTimeout);
                plannerChatClient_ = new OpenAI.Chat.ChatClient("gpt-4o-mini", new ApiKeyCredential("XXX"), options);
                planner_ = plannerChatClient_.AsAIAgent(
                    options: new ChatClientAgentOptions()
                    {
                        Name = (string)config["planner_name"],
                        ChatOptions = new()
                        {
                            Temperature = (float)temperature,
                            Instructions = (string)config["planner_instructions"],
                            Reasoning = new ReasoningOptions
                            {
                                Effort = (ReasoningEffort)Math.Clamp(thinkingEffort, 0, 3),
                                Output = (ReasoningOutput)Math.Clamp(plannerThinkingOutput, 0, 2),
                            },
                            Tools = [AIFunctionFactory.Create(gitLabChanges_.GetChangeFilePaths)]
                        },
                    }
                );
            }

            {
                double temperature = (double)config["executor_temperature"];
                long thinkingEffort = (long)config["executor_thinking_effort"];
                long plannerThinkingOutput = (long)config["executor_thinking_output"];
                long executorTimeout = (long)config["executor_timeout"];

                OpenAIClientOptions options = new OpenAIClientOptions();
                options.Endpoint = new Uri((string)config["executor"]);
                options.NetworkTimeout = TimeSpan.FromSeconds(executorTimeout);
                executorChatClient_ = new OpenAI.Chat.ChatClient("gpt-4o-mini", new ApiKeyCredential("XXX"), options);
                executor_ = executorChatClient_.AsAIAgent(
                    options: new ChatClientAgentOptions()
                    {
                        Name = (string)config["executor_name"],
                        ChatOptions = new()
                        {
                            Temperature = (float)temperature,
                            Instructions = (string)config["executor_instructions"],
                            Reasoning = new ReasoningOptions
                            {
                                Effort = (ReasoningEffort)Math.Clamp(thinkingEffort, 0, 3),
                                Output = (ReasoningOutput)Math.Clamp(plannerThinkingOutput, 0, 2),
                            }
                        },
                    }
                );
            }
        }

        public Tools.GitLabChanges GitLabChanges => gitLabChanges_;

        public async Task BeginSessionAsync(string prompt, CancellationToken cancellationToken)
        {
            session_ = await planner_.CreateSessionAsync(cancellationToken);
        }

        public async Task<AgentResponse> RunPlannerAsync(string prompt, CancellationToken cancellationToken)
        {
            AgentResponse response = await planner_.RunAsync(prompt, session_, runOptions_, cancellationToken);
            return response;
        }

        public async Task<T> RunPlannerAsync<T>(string prompt, CancellationToken cancellationToken)
        {
            runOptions_.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(AIJsonUtilities.CreateJsonSchema(typeof(T)));
            AgentResponse response = await planner_.RunAsync(prompt, session_, runOptions_, cancellationToken);
            runOptions_.ResponseFormat = null;
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(response.Text);
        }

        public async Task<AgentResponse> RunExecutorAsync(string prompt, CancellationToken cancellationToken)
        {
            AgentResponse response = await executor_.RunAsync(prompt);
            return response;
        }

        public async Task<T?> RunExecutorAsync<T>(string prompt, CancellationToken cancellationToken)
        {
            try {
            runOptions_.ResponseFormat = Microsoft.Extensions.AI.ChatResponseFormat.ForJsonSchema(AIJsonUtilities.CreateJsonSchema(typeof(T)));
            AgentResponse response = await executor_.RunAsync(prompt, session_, runOptions_, cancellationToken);
            runOptions_.ResponseFormat = null;
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(response.Text);
            }
            catch
            {
                return default;
            }
        }

        private OpenAI.Chat.ChatClient plannerChatClient_;
        private AIAgent planner_;
        private OpenAI.Chat.ChatClient executorChatClient_;
        private AIAgent executor_;
        private AgentRunOptions runOptions_ = new AgentRunOptions();
        private Tools.GitLabChanges gitLabChanges_ = new Tools.GitLabChanges();
        private AgentSession? session_;
    }
}
