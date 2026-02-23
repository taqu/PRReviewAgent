
using Microsoft.Extensions.Logging;
using PRReviewAgent.Services;

namespace PRReviewAgent
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (!Settings.Initialize())
            {
                return;
            }

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();
            {
                Tomlyn.Model.TomlTable? server = (Tomlyn.Model.TomlTable)Settings.Instance.Config["server"];
                builder.WebHost.UseUrls($"http://{server["host"]}:{server["port"]}");

                if(server.TryGetValue("log_level", out object? log_level))
                {
                    LogLevel level;
                    if(Enum.TryParse(log_level.ToString(), true, out level))
                    {
                        builder.Logging.SetMinimumLevel(level);
                    }
                }
            }

            // Add services to the container.

            builder.Services.AddControllers();
            //builder.Services.AddOpenApi();
            builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            builder.Services.AddHostedService<QueuedProcessorBackgroundService>();
            try
            {
                string url = string.Empty;
                string accessToken = string.Empty;
                Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Settings.Instance.Config["gitlab"];
                url = (string)config["url"];
                Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Settings.Instance.Secrets["gitlab"];
                accessToken = (string)secrets["personal_access_token"];
                GitLabClientService gitLabClientService = new GitLabClientService(url, accessToken);
                builder.Services.AddSingleton<GitLabClientService>(gitLabClientService);
            }
            catch (Exception ex)
            {
                return;
            }
            WebApplication app = builder.Build();

            // Configure the HTTP request pipeline.
            //if (app.Environment.IsDevelopment())
            //{
            //    app.MapOpenApi();
            //}

            app.MapControllers();

            app.Run();
        }
    }
}
