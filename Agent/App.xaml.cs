using System.Configuration;
using System.Data;
using System.Windows;

namespace Agent
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private void Application_Startup(object sender, StartupEventArgs e)
        {
            Context.Initialize("http://192.168.128.152:9090/v1/");
        }
    }

}
