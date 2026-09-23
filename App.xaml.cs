using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using DeepLocal.Services;
using WF = System.Windows.Forms;

namespace DeepLocal
{
    public partial class App : System.Windows.Application
    {
        public static bool AllowClose { get; private set; }
        public static App? Instance { get; private set; }

        private const string MutexName = "DeepLocal_OfflineTranslator_SingleInstance";
        private const string ShowEventName = "DeepLocal_OfflineTranslator_Show";

        private WF.NotifyIcon? _tray;
        private Mutex? _singleInstanceMutex;
        private EventWaitHandle? _showEvent;
        private WF.ToolStripMenuItem? _miOpen, _miHide, _miClipboard, _miExit;

        // ===== DPI =====
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private static double GetScale(Window w)
        {
            try
            {
                var hwnd = new WindowInteropHelper(w).Handle;
                if (hwnd != IntPtr.Zero) return GetDpiForWindow(hwnd) / 96.0;
            }
            catch { }
            return 1.0;
        }

        /// <summary>Ancora la finestra in basso a destra dell'area di lavoro, rimpicciolendola se lo schermo è piccolo.</summary>
        public static void PlaceWindowBottomRight(Window w, double marginDip = 16.0)
        {
            if (w == null) return;

            var hwnd = new WindowInteropHelper(w).Handle;
            var screen = hwnd != IntPtr.Zero ? WF.Screen.FromHandle(hwnd) : WF.Screen.FromPoint(WF.Cursor.Position);
            var wa = screen.WorkingArea;
            double scale = GetScale(w);

            double waLeft = wa.Left / scale, waTop = wa.Top / scale;
            double waWidth = wa.Width / scale, waHeight = wa.Height / scale;

            // Su un portatile al 150% la finestra da 1100x660 non ci starebbe: si adatta all'area disponibile
            double maxW = Math.Max(w.MinWidth, waWidth - 2 * marginDip);
            double maxH = Math.Max(w.MinHeight, waHeight - 2 * marginDip);
            if (w.Width > maxW) w.Width = maxW;
            if (w.Height > maxH) w.Height = maxH;

            double width = !double.IsNaN(w.Width) && w.Width > 0 ? w.Width : w.ActualWidth;
            double height = !double.IsNaN(w.Height) && w.Height > 0 ? w.Height : w.ActualHeight;

            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = Math.Max(waLeft, waLeft + waWidth - marginDip - width);
            w.Top = Math.Max(waTop, waTop + waHeight - marginDip - height);
        }

        public static void ResnapAfterFirstLayout(Window w)
        {
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (w.ActualWidth <= 0 || w.ActualHeight <= 0) return;
                w.LayoutUpdated -= handler!;
                PlaceWindowBottomRight(w);
            };
            w.LayoutUpdated += handler;
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Instance = this;

            _singleInstanceMutex = new Mutex(true, MutexName, out bool createdNew);
            if (!createdNew)
            {
                // Già aperto: si chiede all'istanza esistente di mostrarsi, e si esce
                try
                {
                    using var ev = EventWaitHandle.OpenExisting(ShowEventName);
                    ev.Set();
                }
                catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
                Shutdown();
                return;
            }

            Loc.I.SetLanguage(Settings.Load().Ui);
            ThemeManager.Initialize();
            ListenForSecondInstance();
            CreateTray();

            var win = new MainWindow();
            MainWindow = win;
            PlaceWindowBottomRight(win);
            win.SourceInitialized += (_, _) => PlaceWindowBottomRight(win);

            // Con "Avvia con Windows" parte nella tray, senza finestra
            bool minimized = e.Args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
            if (minimized)
            {
                // Crea l'handle (serve per Alt+T) senza mostrare la finestra
                new WindowInteropHelper(win).EnsureHandle();
            }
            else
            {
                win.Show();
                win.Activate();
            }
            win.ReportHotkeyIfFailed();
            UpdateMenuItems();
        }

        private void ListenForSecondInstance()
        {
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            var thread = new Thread(() =>
            {
                while (_showEvent != null)
                {
                    try
                    {
                        if (!_showEvent.WaitOne()) break;
                    }
                    catch { break; }
                    Dispatcher.BeginInvoke(ShowMainWindow);
                }
            })
            { IsBackground = true, Name = "DeepLocal second-instance listener" };
            thread.Start();
        }

        private void CreateTray()
        {
            _tray = new WF.NotifyIcon { Visible = true, Icon = LoadTrayIcon() };

            var menu = new WF.ContextMenuStrip();
            _miOpen = new WF.ToolStripMenuItem("", null, (_, _) => ShowMainWindow());
            _miClipboard = new WF.ToolStripMenuItem("", null, async (_, _) =>
            {
                if (MainWindow is MainWindow mw) await mw.TranslateClipboardAsync();
            });
            _miHide = new WF.ToolStripMenuItem("", null, (_, _) => HideMainWindow());
            _miExit = new WF.ToolStripMenuItem("", null, (_, _) =>
            {
                AllowClose = true;
                Shutdown();
            });
            menu.Items.Add(_miOpen);
            menu.Items.Add(_miClipboard);
            menu.Items.Add(_miHide);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(_miExit);
            menu.Opening += (_, _) => UpdateMenuItems();
            _tray.ContextMenuStrip = menu;

            _tray.MouseClick += (_, a) =>
            {
                // Clic sinistro: mostra e porta in primo piano (si nasconde con la X o dal menu)
                if (a.Button == WF.MouseButtons.Left) ShowMainWindow();
            };

            RefreshTrayTexts();
        }

        public void RefreshTrayTexts()
        {
            if (_tray == null) return;
            _tray.Text = Loc.T("TrayText");
            if (_miOpen != null) _miOpen.Text = Loc.T("TrayOpen");
            if (_miClipboard != null) _miClipboard.Text = Loc.T("TrayClipboard");
            if (_miHide != null) _miHide.Text = Loc.T("TrayHide");
            if (_miExit != null) _miExit.Text = Loc.T("TrayExit");
        }

        private static System.Drawing.Icon LoadTrayIcon()
        {
            try
            {
                string ico = Path.Combine(AppContext.BaseDirectory, "Assets", "DeepLocal_UserIcon_Framed.ico");
                if (File.Exists(ico)) return new System.Drawing.Icon(ico, WF.SystemInformation.SmallIconSize);
            }
            catch { }
            return System.Drawing.SystemIcons.Application;
        }

        private void ShowMainWindow()
        {
            if (MainWindow is MainWindow mw) mw.ShowAndActivate();
            UpdateMenuItems();
        }

        private void HideMainWindow()
        {
            MainWindow?.Hide();
            UpdateMenuItems();
        }

        public void UpdateMenuItems()
        {
            bool visible = MainWindow is { IsVisible: true };
            if (_miOpen != null) _miOpen.Enabled = !visible;
            if (_miHide != null) _miHide.Enabled = visible;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            AllowClose = true;

            if (_tray != null)
            {
                try { _tray.Visible = false; _tray.Dispose(); } catch { }
                _tray = null;
            }

            var ev = _showEvent;
            _showEvent = null;
            try { ev?.Set(); ev?.Dispose(); } catch { }

            if (_singleInstanceMutex != null)
            {
                try { _singleInstanceMutex.ReleaseMutex(); } catch { }
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
            }

            base.OnExit(e);
        }
    }
}
