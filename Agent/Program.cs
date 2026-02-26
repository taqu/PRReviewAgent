using Spectre.Console;

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
            AnsiConsole.Markup("[underline red]こんにちは[/] world!\n");

            var table = new Table();
            {
                table.AddColumn("Foo");
                table.AddColumn(new TableColumn("Bar").Centered());

                table.AddRow("行１列１", "[green]行１列２[/]");
                table.AddRow(new Markup("[blue]行２列１[/]"), new Panel("行２列２"));
            }
            AnsiConsole.Write(table);
        }
    }
}
