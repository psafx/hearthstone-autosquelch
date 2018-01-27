using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using static Hearthstone_Deck_Tracker.User32;
using static Hearthstone_Deck_Tracker.User32.MouseEventFlags;

namespace Autosquelch
{
    public class MouseHelpers
    {
        const int DefaultClickDelayMs = 70;

        public static async Task ClickOnPoint(IntPtr wndHandle, Point clientPoint, bool leftMouseButton)
        {
            ClientToScreen(wndHandle, ref clientPoint);

            SetCursorPosition(clientPoint);
            Hearthstone_Deck_Tracker.Utility.Logging.Log.Debug("Clicking " + Cursor.Position);

            if (SystemInformation.MouseButtonsSwapped)
            {
                leftMouseButton = !leftMouseButton;
            }

            //mouse down
            if (leftMouseButton)
                mouse_event((uint)LeftDown, 0, 0, 0, UIntPtr.Zero);
            else
                mouse_event((uint)RightDown, 0, 0, 0, UIntPtr.Zero);

            await Task.Delay(DefaultClickDelayMs);

            //mouse up
            if (leftMouseButton)
                mouse_event((uint)LeftUp, 0, 0, 0, UIntPtr.Zero);
            else
                mouse_event((uint)RightUp, 0, 0, 0, UIntPtr.Zero);

            await Task.Delay(DefaultClickDelayMs / 2);
        }

        public static void SetCursorPosition(Point point)
        {
            Cursor.Position = new Point(point.X, point.Y);
        }
    }
}
