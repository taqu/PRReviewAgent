using Terminal.Gui.App;
using Terminal.Gui.Configuration;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Agent
{
    public class MainWindow : Runnable<string?>
    {
        public MainWindow()
        {
            Title = "Agent";
            FrameView inputFrameView = new()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 6,
            };

            inputView_ = new()
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 4,
            };
            inputView_.KeyDown += new EventHandler<Key>(OnKeyDown);
            inputFrameView.Add(inputView_);

            outputView_ = new()
            {
                X = 0,
                Y = Pos.Bottom(inputFrameView),
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Multiline = true,
                ReadOnly = true,
                Text = "Hello World!",
            };
            this.Add(inputFrameView);
            this.Add(outputView_);
        }

        private void OnKeyDown(object? sender, Key key)
        {
            if(key == Key.Enter)
            {
            }
        }

        private TextView inputView_;
        private TextView outputView_;
    }
}
