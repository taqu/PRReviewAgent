using Microsoft.Agents.AI;
using OpenAI.Realtime;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Agent
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Input.Focus();
        }

        private void InputOnKeyDown(object sender, KeyEventArgs e)
        {
            if(e.Key == Key.Enter)
            {
                if(e.KeyboardDevice.Modifiers != ModifierKeys.None)
                {
                    e.Handled = true;
                    string prompt = Input.Text.Trim();
                    if (!string.IsNullOrEmpty(prompt))
                    {
                        Input.Clear();
                        Task.Run(async () => await Context.Instance.Agents.RunAsync(prompt, Context.Instance.CancellationToken));
                    }
                    return;
                }
                else
                {
                    Input.AppendText(System.Environment.NewLine);
                }
            }
        }

        public void Push(AgentResponse response)
        {
        }
    }
}