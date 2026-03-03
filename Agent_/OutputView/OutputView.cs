using Terminal.Gui.Views;

namespace Agent
{
    public class OutputView : TextView
    {
        public void AppendText(string text)
        {
            Terminal.Gui.Drivers.Cursor cursor = Cursor;
            MoveEnd();
            Terminal.Gui.Drivers.Cursor currentEnd = Cursor;
            InsertText(text);
            if (currentEnd != cursor)
            {
                Cursor = cursor;
                ScrollTo(cursor.Position.Value.Y);
            }
        }
    }
}
