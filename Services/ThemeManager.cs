using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;

namespace DeepLocal.Services
{
    /// <summary>Tema chiaro/scuro che segue Windows, cambiato al volo quando l'utente lo cambia.</summary>
    public static class ThemeManager
    {
        public static bool IsDark { get; private set; }

        public static event Action? ThemeChanged;

        public static void Initialize()
        {
            Apply(ReadWindowsIsDark());
            SystemEvents.UserPreferenceChanged += (_, e) =>
            {
                if (e.Category != UserPreferenceCategory.General) return;
                var dark = ReadWindowsIsDark();
                if (dark == IsDark) return;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Apply(dark));
            };
        }

        private static bool ReadWindowsIsDark()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
            }
            catch { return false; }
        }

        private static void Apply(bool dark)
        {
            IsDark = dark;
            var app = System.Windows.Application.Current;
            if (app == null) return;

            var dict = new ResourceDictionary
            {
                Source = new Uri($"pack://application:,,,/Themes/{(dark ? "Dark" : "Light")}.xaml")
            };
            var merged = app.Resources.MergedDictionaries;
            // Il dizionario dei colori è sempre il primo; gli stili dei controlli vengono dopo.
            if (merged.Count > 0) merged[0] = dict; else merged.Add(dict);

            foreach (Window w in app.Windows) ApplyTitleBar(w);
            ThemeChanged?.Invoke();
        }

        // ===== Barra del titolo di Windows 11 dello stesso blu notte della testata =====
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        public static void ApplyTitleBar(Window w)
        {
            var hwnd = new WindowInteropHelper(w).Handle;
            if (hwnd == IntPtr.Zero) return;
            try
            {
                int on = 1;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                int caption = 0x0027170E; // #0E1727 in formato COLORREF (0x00BBGGRR)
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));
                int text = 0x00FFFFFF;
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));
            }
            catch
            {
                // Windows 10: gli attributi non esistono, la barra resta quella di sistema.
            }
        }
    }
}
