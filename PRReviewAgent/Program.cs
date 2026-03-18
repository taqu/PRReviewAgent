
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PRReviewAgent.Services;
using System;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace PRReviewAgent
{
    public class Program
    {
        private static bool RemoteCertificateValidationCallback(
            Object sender,
            X509Certificate certificate,
            X509Chain chain,
            SslPolicyErrors sslPolicyErrors)
        {
            if(SslPolicyErrors.None == sslPolicyErrors)
            {
                return true;
            }
            Tomlyn.Model.TomlTable config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["server"];
            if ((bool)config["trust_certificate"])
            {
                return true;
            }
            string[] trusted_certificates = (string[])config["trusted_certificates"];
            foreach (string cert in trusted_certificates)
            {
                if(cert == certificate.Subject)
                {
                    return true;
                }
            }
            return false;
        }

        public static void Main(string[] args)
        {
            if (!Context.Initialize())
            {
                return;
            }
            System.Net.ServicePointManager.ServerCertificateValidationCallback += RemoteCertificateValidationCallback;

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
            builder.Services.AddSingleton<IBackgroundTaskQueue, BackgroundTaskQueue>();
            builder.Services.AddHostedService<QueuedProcessorBackgroundService>();
            bool ssl_verify = false;
            try
            {
                switch (Context.Instance.GitProvider) {
                case "github":
                        {
                            Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["github"];
                            Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["github"];
                            GitHubClientService gitHubClientService = new GitHubClientService((string)config["name"], (string)secrets["personal_access_token"]);
                            builder.Services.AddSingleton<GitHubClientService>(gitHubClientService);
                            ssl_verify = (bool)config["ssl_verify"];
                        }
                        break;
                case "gitlab":
                        {
                    Tomlyn.Model.TomlTable? config = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["gitlab"];
                    Tomlyn.Model.TomlTable? secrets = (Tomlyn.Model.TomlTable)Context.Instance.Settings.Secrets["gitlab"];
                    GitLabClientService gitLabClientService = new GitLabClientService((string)config["url"], (string)secrets["personal_access_token"]);
                    builder.Services.AddSingleton<GitLabClientService>(gitLabClientService);
                    ssl_verify = (bool)config["ssl_verify"];
                    }
                        break;
                }
                if (ssl_verify)
                {
                    builder.Services.AddAuthentication(
                        CertificateAuthenticationDefaults.AuthenticationScheme)
                    .AddCertificate();
                }

            }
            catch (Exception ex)
            {
                return;
            }
            if ((bool)((Tomlyn.Model.TomlTable)Context.Instance.Settings.Config["common"])["warm_up"])
            {
                builder.Services.AddHostedService<WarmUpTask>();
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
