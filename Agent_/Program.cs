using Terminal.Gui.App;

namespace Agent
{
    internal class Program
    {
        #if false
        static void Main(string[] args)
        {
            OpenAIClientOptions options = new OpenAIClientOptions();
            options.Endpoint = new Uri("http://192.168.128.152:9090/v1/");
            ChatClient client = new OpenAI.Chat.ChatClient("gpt-4o-mini", new ApiKeyCredential("XXX"), options);
            Tools.WeatherForecast weatherForecast = new Tools.WeatherForecast();
            AITool tool = AIFunctionFactory.Create(weatherForecast.GetWeather);
            AIAgent agent = client.AsAIAgent(
                new ChatClientAgentOptions(){
                    Name = "HelloAgent",
                    ChatOptions = new()
                    {
                        Temperature = 0.1f,
                        Instructions = "You are a friendly assistant. Keep your answers brief.",
                        Tools = [tool],
                    },
            });

            Task.Run(async () => Console.WriteLine(await agent.RunAsync("What is the weather like in Amsterdam?"))).Wait();
        }
        #endif
        static void Main(string[] args)
        {
            using IApplication app = Application.Create();
            app.Init();
            app.Run<MainWindow>();
        }
    }
}
