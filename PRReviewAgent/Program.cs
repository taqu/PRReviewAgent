
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.Logging;
using PRReviewAgent.Services;
using System;

namespace PRReviewAgent
{
    public class Program
    {
        public static void Main(string[] args)
        {
            if (!Context.Initialize())
            {
                return;
            }

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.Logging.ClearProviders();
            builder.Logging.AddConsole();

            {
                Tomlyn.Model.TomlTable? server = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["server"];
                builder.WebHost.UseUrls($"{server["url"]}");

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
            bool ssl_verify = false;
            try
            {
                {
                    Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["gitlab"];
                    Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["gitlab"];
                    GitLabClientService gitLabClientService = new GitLabClientService((string)config["url"], (string)secrets["personal_access_token"]);
                    builder.Services.AddSingleton<GitLabClientService>(gitLabClientService);

                    ssl_verify = (bool)config["ssl_verify"];
                    if (ssl_verify)
                    {
                        builder.Services.AddAuthentication(
                            CertificateAuthenticationDefaults.AuthenticationScheme)
                        .AddCertificate();
                    }
                }
            }
            catch (Exception ex)
            {
                return;
            }
            WebApplication app = builder.Build();
            if (ssl_verify)
            {
                app.UseAuthentication();
            }

            app.MapControllers();

            app.Run();
        }
    }
}
