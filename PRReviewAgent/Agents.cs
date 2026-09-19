using NGitLab;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Unicode;
using System.Xml.Schema;

namespace PRReviewAgent
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    public class LlmSchemaAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        public LlmSchemaAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

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
        private static void Build(out OpenAI.Chat.ChatClient chatClient, out string instructions, out string model, ChatCompletionOptions chatCompletionOptions, string name)
        {
            // Retrieve OpenAI API key from secrets
            Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["openai"];
            string apiKey = (string)secrets["api_key"];

            // Load agent-specific configuration settings
            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["agent"];
            model = (string)config[$"{name}_model"];
            long max_output = (long)config[$"{name}_max_output"];
            if (!model.StartsWith("gpt-"))
            {
                double temperature = (double)config[$"{name}_temperature"];
                double topp = (double)config[$"{name}_topp"];
                //long topk = (long)config[$"{name}_topk"];
                double frequencyPenalty = (double)config[$"{name}_frequency_penalty"];

                chatCompletionOptions.Temperature = (float)temperature;
                chatCompletionOptions.TopP = (float)topp;
                //chatCompletionOptions.FrequencyPenalty = (float)frequencyPenalty;
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
            chatCompletionOptions.ToolChoice = ChatToolChoice.CreateNoneChoice();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Agents"/> class, building the default set of agents.
        /// </summary>
        public Agents()
        {
            // Build the three core agents: assistant, planner, and executor
            Build(out chatClient_, out instructions_, out model_, chatCompletionOptions_, "reviewer");
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
            OpenAI.Chat.ChatMessage[] messages = CreateChatMessages(prompt);
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
        public async Task<string> RunAsync(string prompt, bool reasoning, CancellationToken cancellationToken)
        {
            // Execute the agent and return the raw response
            ClientResult<ChatCompletion>? response;
            if (reasoning || model_.StartsWith("gpt-"))
            {
                OpenAI.Chat.ChatMessage[] messages = CreateChatMessages(prompt);
                ChatCompletionOptions chatCompletionOptions = CloneChatCompletionOptions();
                response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions, cancellationToken);
            }
            else
            {
                var requestPayload = new
                {
                    model = model_,
                    messages = new[] {
                        new { role = "System", content = instructions_ },
                        new { role = "User", content = prompt }
                    },
                    chat_template_kwargs = new
                    {
                        enable_thinking = false,
                        tool_choice = "none",
                    }
                };
                try
                {
                    var options = new JsonSerializerOptions
                    {
                        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
                        WriteIndented = false
                    };
                    string jsonString = JsonSerializer.Serialize(requestPayload, options);
                    BinaryContent content = BinaryContent.Create(BinaryData.FromString(jsonString));
                    ClientResult clientResult = await chatClient_.CompleteChatAsync(content);
#pragma warning disable OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
                    ChatCompletion chatCompletion = (ChatCompletion)clientResult;
#pragma warning restore OPENAI001 // 種類は、評価の目的でのみ提供されています。将来の更新で変更または削除されることがあります。続行するには、この診断を非表示にします。
                    response = ClientResult.FromValue(chatCompletion, clientResult.GetRawResponse());
                }
                catch
                {
                    return string.Empty;
                }
            }
#if DEBUG
            try
            {
                string jsonText = response.GetRawResponse().Content.ToString();
                using (JsonDocument doc = JsonDocument.Parse(response.GetRawResponse().Content))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        if (choices[0].TryGetProperty("message", out JsonElement message))
                        {
                            if (message.TryGetProperty("reasoning_content", out JsonElement reasoningElement))
                            {
                                string reasoningContent = reasoningElement.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(reasoningContent))
                                {
                                    Context.Instance.Log(LogLevel.Information, reasoningContent);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
#endif
            Context.Instance.Log(LogLevel.Information, $"input tokens:{response.Value.Usage.InputTokenCount} output tokens:{response.Value.Usage.OutputTokenCount} total tokens:{response.Value.Usage.TotalTokenCount}");
            if (response.Value.Content.Count <= 0)
            {
                return string.Empty;
            }
            return response.Value.Content[0].Text;
        }

        private static void FixSchemaForOpenAi(JsonNode? node)
        {
            if (node is not JsonObject obj) return;

            // Convert array types like ["object", "null"] to a single string type
            if (obj.TryGetPropertyValue("type", out var typeNode))
            {
                // If the type is in an array format such as ["object", "null"] or ["string", "null"]
                if (typeNode is JsonArray typeArray)
                {
                    // Extract a valid type other than "null" (e.g., "object", "string") from the array
                    var actualType = typeArray.FirstOrDefault(x => x?.ToString() != "null")?.ToString();
                    if (actualType != null)
                    {
                        // Overwrite the array with a single string value (e.g., "object")
                        obj["type"] = JsonValue.Create(actualType);
                    }
                }
            }

            // Retrieve the current type string for evaluation
            string? typeString = obj["type"]?.ToString();

            // If the type is object or contains properties
            if (typeString == "object" || obj.ContainsKey("properties"))
            {
                // 1. Force additionalProperties to false
                obj["additionalProperties"] = false;

                // 2. Mark all properties as required
                if (obj.TryGetPropertyValue("properties", out var propsNode) && propsNode is JsonObject propsObj)
                {
                    var requiredArray = new JsonArray();
                    foreach (var prop in propsObj)
                    {
                        requiredArray.Add(prop.Key);
                        // Recursively process child elements (nested objects or arrays)
                        FixSchemaForOpenAi(prop.Value);
                    }
                    obj["required"] = requiredArray;
                }
            }
            // If the type is an array, recursively process the contents of items
            else if (typeString == "array")
            {
                if (obj.TryGetPropertyValue("items", out var itemsNode))
                {
                    FixSchemaForOpenAi(itemsNode);
                }
            }
        }

        /// <summary>
        /// Runs the specified agent asynchronously with a prompt and returns the result deserialized to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the agent's response into.</typeparam>
        /// <param name="prompt">The prompt to send to the agent.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized response of type <typeparamref name="T"/>.</returns>
        public async Task<T?> RunAsync<T>(string prompt, CancellationToken cancellationToken) where T : class
        {
            // Set the expected response format to JSON schema based on type T
            Type type = typeof(T);
            LlmSchemaAttribute? attribute = (LlmSchemaAttribute?)Attribute.GetCustomAttribute(type, typeof(LlmSchemaAttribute));
            string schemaName = attribute?.Name ?? $"{type.Name}_schema";
            string? schemaDescription = attribute?.Description;

            JsonNode schemaNode = System.Text.Json.JsonSerializerOptions.Default.GetJsonSchemaAsNode(type);
            // Correct metadata for OpenAI
            if (schemaNode is JsonObject rootObj)
            {
                rootObj.Remove("$schema");
                rootObj.Remove("$id");
                rootObj.Remove("$comment");
            }
            FixSchemaForOpenAi(schemaNode);

            BinaryData schemaData = BinaryData.FromString(schemaNode.ToString());

            ChatResponseFormat jsonFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                jsonSchemaFormatName: schemaName,
                jsonSchema: schemaData,
                jsonSchemaFormatDescription: schemaDescription,
                jsonSchemaIsStrict: true
            );
            ChatCompletionOptions chatCompletionOptions = CloneChatCompletionOptions();
            chatCompletionOptions.ResponseFormat = jsonFormat;

            OpenAI.Chat.ChatMessage[] messages = CreateChatMessages(prompt);

            ClientResult<ChatCompletion> response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions, cancellationToken);
            if (response.Value.Content.Count <= 0)
            {
                return null;
            }

#if DEBUG
            try
            {
                string jsonText = response.GetRawResponse().Content.ToString();
                using (JsonDocument doc = JsonDocument.Parse(response.GetRawResponse().Content))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        if (choices[0].TryGetProperty("message", out JsonElement message))
                        {
                            if (message.TryGetProperty("reasoning_content", out JsonElement reasoningElement))
                            {
                                string reasoningContent = reasoningElement.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(reasoningContent))
                                {
                                    Context.Instance.Log(LogLevel.Information, reasoningContent);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
#endif

            try
            {
                Context.Instance.Log(LogLevel.Information, $"input tokens:{response.Value.Usage.InputTokenCount} output tokens:{response.Value.Usage.OutputTokenCount} total tokens:{response.Value.Usage.TotalTokenCount}");
                return System.Text.Json.JsonSerializer.Deserialize<T>(response.Value.Content[0].Text);
            }
            catch
            {
                return null;
            }
        }

        private static ReadOnlySpan<char> CleanJson(ReadOnlySpan<char> text)
        {
            text = text.Trim();
            if (text.IsEmpty){
                return text;
            }

            int start = text.IndexOf('{');
            if (start == -1){
                start = text.IndexOf('[');
            }

            int end = text.LastIndexOf('}');
            if (end == -1){
                end = text.LastIndexOf(']');
            }

            if (start != -1 && end != -1 && end > start)
            {
                return text.Slice(start, end - start + 1);
            }
            return text;
        }

        /// <summary>
        /// Runs the specified agent asynchronously with a prompt and returns the result deserialized to the specified type.
        /// </summary>
        /// <typeparam name="T">The type to deserialize the agent's response into.</typeparam>
        /// <param name="type">The type of agent to run.</param>
        /// <param name="prompt">The prompt to send to the agent.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the deserialized response of type <typeparamref name="T"/>.</returns>
        public async Task<T?> RunJsonAsync<T>(string prompt, CancellationToken cancellationToken) where T : class
        {
            ChatCompletionOptions chatCompletionOptions = CloneChatCompletionOptions();

            OpenAI.Chat.ChatMessage[] messages = CreateChatMessages(prompt);

            ClientResult<ChatCompletion> response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions, cancellationToken);
            if (response.Value.Content.Count <= 0)
            {
                return null;
            }

#if DEBUG
            try
            {
                string jsonText = response.GetRawResponse().Content.ToString();
                using (JsonDocument doc = JsonDocument.Parse(response.GetRawResponse().Content))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        if (choices[0].TryGetProperty("message", out JsonElement message))
                        {
                            if (message.TryGetProperty("reasoning_content", out JsonElement reasoningElement))
                            {
                                string reasoningContent = reasoningElement.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(reasoningContent))
                                {
                                    Context.Instance.Log(LogLevel.Information, reasoningContent);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
#endif

            try
            {
                Context.Instance.Log(LogLevel.Information, $"input tokens:{response.Value.Usage.InputTokenCount} output tokens:{response.Value.Usage.OutputTokenCount} total tokens:{response.Value.Usage.TotalTokenCount}");
                ReadOnlySpan<char> json = CleanJson(response.Value.Content[0].Text);
                return System.Text.Json.JsonSerializer.Deserialize<T>(json);
            }
            catch
            {
                return null;
            }
        }


        /// <summary>The configured model identifier for the reviewer agent.</summary>
        public string Model => model_;

        /// <summary>
        /// Runs RunJsonAsync but also returns token usage from the provider response.
        /// Returns null Value if the response is empty or cannot be deserialized.
        /// </summary>
        public async Task<(T? Value, int? InputTokens, int? OutputTokens)> RunJsonWithUsageAsync<T>(string prompt, CancellationToken cancellationToken) where T : class
        {
            ChatCompletionOptions chatCompletionOptions = CloneChatCompletionOptions();
            OpenAI.Chat.ChatMessage[] messages = CreateChatMessages(prompt);
            ClientResult<ChatCompletion> response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions, cancellationToken);
            int? inputTokens = null, outputTokens = null;
            try
            {
                inputTokens = response.Value.Usage?.InputTokenCount;
                outputTokens = response.Value.Usage?.OutputTokenCount;
                Context.Instance.Log(LogLevel.Information, $"input tokens:{inputTokens} output tokens:{outputTokens}");
            }
            catch { }
#if DEBUG
            try
            {
                string jsonText = response.GetRawResponse().Content.ToString();
                using (JsonDocument doc = JsonDocument.Parse(response.GetRawResponse().Content))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        if (choices[0].TryGetProperty("message", out JsonElement message))
                        {
                            if (message.TryGetProperty("reasoning_content", out JsonElement reasoningElement))
                            {
                                string reasoningContent = reasoningElement.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(reasoningContent))
                                {
                                    Context.Instance.Log(LogLevel.Information, reasoningContent);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
#endif
            if (response.Value.Content.Count <= 0) return (null, inputTokens, outputTokens);
            try
            {
                ReadOnlySpan<char> json = CleanJson(response.Value.Content[0].Text);
                T? value = System.Text.Json.JsonSerializer.Deserialize<T>(json);
                return (value, inputTokens, outputTokens);
            }
            catch
            {
                return (null, inputTokens, outputTokens);
            }
        }

        /// <summary>
        /// Runs RunAsync(reasoning=false) but also returns token usage from the provider response.
        /// </summary>
        public async Task<(string Value, int? InputTokens, int? OutputTokens)> RunWithUsageAsync(string prompt, CancellationToken cancellationToken)
        {
            OpenAI.Chat.ChatMessage[] messages = CreateChatMessages(prompt);
            ChatCompletionOptions chatCompletionOptions = CloneChatCompletionOptions();
            ClientResult<ChatCompletion>? response;
            if (model_.StartsWith("gpt-"))
            {
                response = await chatClient_.CompleteChatAsync(messages, chatCompletionOptions, cancellationToken);
            }
            else
            {
                var requestPayload = new
                {
                    model = model_,
                    messages = new[] {
                        new { role = "System", content = instructions_ },
                        new { role = "User", content = prompt }
                    },
                    chat_template_kwargs = new
                    {
                        enable_thinking = false,
                        tool_choice = "none",
                    }
                };
                string jsonString = System.Text.Json.JsonSerializer.Serialize(requestPayload);
                BinaryContent content = BinaryContent.Create(BinaryData.FromString(jsonString));
                ClientResult clientResult = await chatClient_.CompleteChatAsync(content);
#pragma warning disable OPENAI001
                ChatCompletion chatCompletion = (ChatCompletion)clientResult;
#pragma warning restore OPENAI001
                response = ClientResult.FromValue(chatCompletion, clientResult.GetRawResponse());
            }
#if DEBUG
            try
            {
                string jsonText = response.GetRawResponse().Content.ToString();
                using (JsonDocument doc = JsonDocument.Parse(response.GetRawResponse().Content))
                {
                    JsonElement root = doc.RootElement;
                    if (root.TryGetProperty("choices", out JsonElement choices) && choices.GetArrayLength() > 0)
                    {
                        if (choices[0].TryGetProperty("message", out JsonElement message))
                        {
                            if (message.TryGetProperty("reasoning_content", out JsonElement reasoningElement))
                            {
                                string reasoningContent = reasoningElement.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(reasoningContent))
                                {
                                    Context.Instance.Log(LogLevel.Information, reasoningContent);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
#endif
            int? inputTokens = null, outputTokens = null;
            try
            {
                inputTokens = response.Value.Usage?.InputTokenCount;
                outputTokens = response.Value.Usage?.OutputTokenCount;
                Context.Instance.Log(LogLevel.Information, $"input tokens:{inputTokens} output tokens:{outputTokens}");
            }
            catch { }
            if (response.Value.Content.Count <= 0) return (string.Empty, inputTokens, outputTokens);
            return (response.Value.Content[0].Text, inputTokens, outputTokens);
        }

        public ChatMessage[] CreateChatMessages(string message)
        {
            if (string.IsNullOrEmpty(instructions_))
            {
                ChatMessage[] messages = new OpenAI.Chat.ChatMessage[1];
                messages[0] = OpenAI.Chat.ChatMessage.CreateUserMessage(message);
                return messages;
            }
            else
            {
                ChatMessage[] messages = new OpenAI.Chat.ChatMessage[2];
                messages[0] = OpenAI.Chat.ChatMessage.CreateSystemMessage(instructions_);
                messages[1] = OpenAI.Chat.ChatMessage.CreateUserMessage(message);
                return messages;
            }
        }
        public ChatCompletionOptions CloneChatCompletionOptions()
        {
            ChatCompletionOptions chatCompletionOptions = new ChatCompletionOptions();
            chatCompletionOptions.Temperature = chatCompletionOptions_.Temperature;
            chatCompletionOptions.TopP = chatCompletionOptions_.TopP;
            chatCompletionOptions.FrequencyPenalty = chatCompletionOptions_.FrequencyPenalty;
            chatCompletionOptions.ResponseFormat = chatCompletionOptions_.ResponseFormat;
            chatCompletionOptions.MaxOutputTokenCount = chatCompletionOptions_.MaxOutputTokenCount;
            chatCompletionOptions.ToolChoice = chatCompletionOptions_.ToolChoice;
            return chatCompletionOptions;
        }
        private OpenAI.Chat.ChatClient chatClient_;
        private ChatCompletionOptions chatCompletionOptions_ = new ChatCompletionOptions();
        private string model_ = string.Empty;
        private string instructions_ = string.Empty;
    }
}
