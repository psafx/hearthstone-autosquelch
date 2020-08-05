using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Utility;
using Hearthstone_Deck_Tracker.Utility.HotKeys;
using Core = Hearthstone_Deck_Tracker.API.Core;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MenuItem = System.Windows.Controls.MenuItem;
using Point = System.Drawing.Point;

namespace Autosquelch
{
    public class AutosquelchPlugin : IPlugin
    {
        private readonly HotKey DefaultHotKey = new HotKey(ModifierKeys.Control | ModifierKeys.Alt, Keys.D);

        private bool Squelched { get; set; }

        private bool PluginRunning { get; set; }

        private bool AutosquelchDisabled { get; set; }

        private bool ShouldTrySquelch => PluginRunning && GameInProgress && !AutosquelchDisabled;

        private bool GameInProgress => Core.Game != null && Core.Game.IsRunning;

        private bool OpponentIsSquelchable =>
            Core.Game.CurrentGameMode != GameMode.Practice
            && Core.Game.CurrentGameMode != GameMode.None
            && Core.Game.CurrentGameMode != GameMode.Battlegrounds;

        public string Author => "Vasilev Konstantin";

        public string ButtonText => "";

        public string Description =>
            @"When enabled, plugin automatically squelches the opponent at the start of the game.
To temporarily turn off the autosquelch, press Ctrl+Alt+D";

        public MenuItem MenuItem => null;

        public string Name => "Autosquelch";

        public Version Version => new Version(0, 3);

        public void OnButtonPress()
        {
        }

        public void OnLoad()
        {
            Squelched = false;
            PluginRunning = true;

            HotKeyManager.RegisterHotkey(DefaultHotKey, ToggleAutosquelch, "Toggle Autosquelch");

            GameEvents.OnGameStart.Add(() => { Squelched = false; });
            GameEvents.OnTurnStart.Add(activePlayer =>
            {
                if (!Squelched)
                {
                    if (!User32.IsHearthstoneInForeground())
                    {
                        return;
                    }

                    if (!OpponentIsSquelchable)
                    {
                        return;
                    }

                    Squelched = true;
                    var t = Squelch();
                }
            });
        }

        public void OnUnload()
        {
            PluginRunning = false;
            HotKeyManager.RemovePredefinedHotkey(DefaultHotKey);
        }

        public void OnUpdate()
        {
        }

        public async Task Squelch()
        {
            if (!User32.IsHearthstoneInForeground())
            {
                Squelched = false;
                return;
            }

            var hearthstoneWindow = User32.GetHearthstoneWindow();
            if (hearthstoneWindow == IntPtr.Zero)
            {
                return;
            }

            var HsRect = User32.GetHearthstoneRect(true);
            var Ratio = 4.0 / 3.0 / ((double) HsRect.Width / HsRect.Height);
            var opponentHeroPosition = new Point((int) Helper.GetScaledXPos(0.5, HsRect.Width, Ratio), (int) (0.17 * HsRect.Height));
            var squelchBubblePosition = new Point((int) Helper.GetScaledXPos(0.4, HsRect.Width, Ratio), (int) (0.1 * HsRect.Height));
            // setting this as a "width" value relative to height, maybe not best solution?
            const double xScale = 0.051; // 55px @ height = 1080
            const double yScale = 0.025; // 27px @ height = 1080
            const double minBrightness = 0.67;

            var lockWidth = (int) Math.Round(HsRect.Height * xScale);
            var lockHeight = (int) Math.Round(HsRect.Height * yScale);
            var squelchBubbleVisible = false;
            // Limit amount of tries (in case a game mode does not support squelching your opponent or else)
            const int maxTries = 4;
            var timesTried = 0;
            var previousMousePosition = User32.GetMousePos();
            do
            {
                if (!ShouldTrySquelch)
                {
                    Squelched = false;
                    return;
                }

                await MouseHelpers.ClickOnPoint(hearthstoneWindow, opponentHeroPosition, false);

                await Task.Delay(TimeSpan.FromMilliseconds(Config.Instance.OverlayMouseOverTriggerDelay));
                var capture = await ScreenCapture.CaptureHearthstoneAsync(squelchBubblePosition, lockWidth, lockHeight, hearthstoneWindow);
                squelchBubbleVisible = CalculateAverageLightness(capture) > minBrightness;
                if (!squelchBubbleVisible)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(Config.Instance.OverlayMouseOverTriggerDelay));
                    ++timesTried;
                }
            } while (!squelchBubbleVisible && timesTried <= maxTries);

            await MouseHelpers.ClickOnPoint(hearthstoneWindow, squelchBubblePosition, true);
            MouseHelpers.SetCursorPosition(previousMousePosition);
        }

        private void ToggleAutosquelch()
        {
            AutosquelchDisabled = !AutosquelchDisabled;

            // Notify that plugin is active/inactive
            var textBlock = new HearthstoneTextBlock();
            textBlock.FontSize = 14;
            textBlock.Text = "Autosquelch is now " + (AutosquelchDisabled ? "disabled" : "enabled");
            textBlock.Loaded += SetHorizontalPosition;
            Canvas.SetBottom(textBlock, 50);
            var overlay = Core.OverlayCanvas;
            textBlock.HorizontalAlignment = HorizontalAlignment.Center;
            overlay.Children.Add(textBlock);
            Core.OverlayWindow.Update(false);

            const double notificationDurationSeconds = 1.5;
            Task.Delay(TimeSpan.FromSeconds(notificationDurationSeconds)).ContinueWith(_ =>
            {
                Core.OverlayCanvas.Children.Remove(textBlock);
                Core.OverlayWindow.Update(false);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static void SetHorizontalPosition(object s, RoutedEventArgs e)
        {
            var sender = s as HearthstoneTextBlock;
            var textBlockWidth = sender.ActualWidth;
            var canvasWidth = Core.OverlayCanvas.ActualWidth;
            Canvas.SetLeft(sender, (canvasWidth - textBlockWidth) / 2.0);
        }

        /// <summary>
        ///     Calculate average brightness of the bitmap.
        /// </summary>
        /// <param name="bitmap">Bitmap to be processed.</param>
        /// <returns>Brightness from the range 0-1.</returns>
        internal static double CalculateAverageLightness(Bitmap bitmap)
        {
            double lum = 0;
            var width = bitmap.Width;
            var height = bitmap.Height;
            using (var tmpBmp = new Bitmap(bitmap))
            {
                var bppModifier = bitmap.PixelFormat == PixelFormat.Format24bppRgb ? 3 : 4;

                var srcData = tmpBmp.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, bitmap.PixelFormat);
                var stride = srcData.Stride;
                var scan0 = srcData.Scan0;

                //Luminance (standard, objective): (0.2126*R) + (0.7152*G) + (0.0722*B)
                //Luminance (perceived option 1): (0.299*R + 0.587*G + 0.114*B)
                //Luminance (perceived option 2, slower to calculate): sqrt( 0.241*R^2 + 0.691*G^2 + 0.068*B^2 )

                unsafe
                {
                    var p = (byte*) (void*) scan0;

                    for (var y = 0; y < height; y++)
                    {
                        for (var x = 0; x < width; x++)
                        {
                            var idx = y * stride + x * bppModifier;
                            lum += 0.299 * p[idx + 2] + 0.587 * p[idx + 1] + 0.114 * p[idx];
                        }
                    }
                }

                tmpBmp.UnlockBits(srcData);
            }

            var avgLum = lum / (width * height);
            return avgLum / 255.0;
        }
    }
}