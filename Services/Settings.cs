using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace DeepLocal.Services
{
    /// <summary>Preferenze utente in %APPDATA%\DeepLocal\settings.json.</summary>
    public sealed class Settings
    {
        public const string DefaultModel = "translategemma:12b";
        private const string RegistryKey = @"Software\DeepLocal";

        public string Model { get; set; } = DefaultModel;
        public string SourceLang { get; set; } = Language.AutoCode;
        public string TargetLang { get; set; } = "";
        /// <summary>Lingua dell'interfaccia scelta a mano nelle impostazioni ("" = decide l'installer).</summary>
        public string UiLanguage { get; set; } = "";
        public bool AutoTranslate { get; set; } = true;

        /// <summary>Lingua effettiva dell'interfaccia: scelta utente, poi installer, poi Windows.</summary>
        [JsonIgnore] public string Ui { get; set; } = "en";

        private static string FilePath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DeepLocal", "settings.json");

        public static Settings Load()
        {
            Settings s;
            try
            {
                s = File.Exists(FilePath)
                    ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                    : new Settings();
            }
            catch
            {
                s = new Settings();
            }

            s.Ui = s.UiLanguage is "it" or "en"
                ? s.UiLanguage
                : InstallerUiLanguage() ?? (CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "it" ? "it" : "en");

            if (Languages.ByCode(s.TargetLang) is not { IsAuto: false })
                s.TargetLang = s.Ui == "it" ? "it" : "en";

            if (Languages.ByCode(s.SourceLang) == null)
                s.SourceLang = Language.AutoCode;

            if (string.IsNullOrWhiteSpace(s.Model))
                s.Model = DefaultModel;

            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Le preferenze non sono critiche: se il disco rifiuta, si riparte dai default.
            }
        }

        /// <summary>
        /// Lingua scelta dall'installer (ITA o EN): setup.ini accanto all'exe ("UiLanguage=it"),
        /// oppure HKCU\Software\DeepLocal per installazioni fatte a mano.
        /// </summary>
        private static string? InstallerUiLanguage()
        {
            try
            {
                var ini = Path.Combine(AppContext.BaseDirectory, "setup.ini");
                if (File.Exists(ini))
                {
                    foreach (var line in File.ReadAllLines(ini))
                    {
                        var parts = line.Split('=', 2);
                        if (parts.Length == 2 && parts[0].Trim().Equals("UiLanguage", StringComparison.OrdinalIgnoreCase))
                        {
                            var v = parts[1].Trim().ToLowerInvariant();
                            if (v is "it" or "en") return v;
                        }
                    }
                }

                using var key = Registry.CurrentUser.OpenSubKey(RegistryKey);
                var r = key?.GetValue("UiLanguage") as string;
                return r is "it" or "en" ? r : null;
            }
            catch { return null; }
        }

        // ===== Avvio con Windows =====
        // L'installer crea un collegamento in Esecuzione automatica (come la v1.0);
        // dall'app si usa HKCU\...\Run. Si riconoscono entrambi e spegnendo si tolgono entrambi.
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValue = "DeepLocal";

        private static string StartupShortcut =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), "DeepLocal.lnk");

        public static bool StartWithWindows
        {
            get
            {
                try
                {
                    if (File.Exists(StartupShortcut)) return true;
                    using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                    return key?.GetValue(RunValue) != null;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(RunKey);
                    if (value)
                    {
                        if (!File.Exists(StartupShortcut))
                            key.SetValue(RunValue, $"\"{Environment.ProcessPath}\" --minimized");
                    }
                    else
                    {
                        key.DeleteValue(RunValue, false);
                        if (File.Exists(StartupShortcut)) File.Delete(StartupShortcut);
                    }
                }
                catch { }
            }
        }
    }
}
