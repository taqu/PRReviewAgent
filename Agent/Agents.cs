using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace Agent
{
    public class Agents
    {
        public Agents(string url)
        {
            OpenAIClientOptions options = new OpenAIClientOptions();
            options.Endpoint = new Uri(url);//new Uri("http://192.168.128.152:9090/v1/");
            ChatClient client = new OpenAI.Chat.ChatClient("gpt-4o-mini", new ApiKeyCredential("XXX"), options);
            //Tools.WeatherForecast weatherForecast = new Tools.WeatherForecast();
            //AITool tool = AIFunctionFactory.Create(weatherForecast.GetWeather);
            agent_ = client.AsAIAgent(
                new ChatClientAgentOptions(){
                    Name = "HelloAgent",
                    ChatOptions = new()
                    {
                        Temperature = 0.1f,
                        Instructions = "You are a friendly assistant. Keep your answers brief.",
                    },
            });
            agent_.CreateSessionAsync();
        }

        public async Task RunAsync(string prompt, CancellationToken cancellationToken)
        {
            AgentSession session = await agent_.CreateSessionAsync();
            AgentResponse response = await agent_.RunAsync(prompt, session, runOptions_, cancellationToken);
            MainWindow? mainWindow = System.Windows.Application.Current.MainWindow as MainWindow;
            if(null != mainWindow)
            {
                mainWindow.Push(response);
            }
        }

        private OpenAI.Chat.ChatClient chatClient_;
        private AIAgent agent_;
        private AgentRunOptions runOptions_ = new AgentRunOptions();
    }
}
