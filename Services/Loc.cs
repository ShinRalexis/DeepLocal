using System.Collections.Generic;
using System.ComponentModel;

namespace DeepLocal.Services
{
    /// <summary>
    /// Testi dell'interfaccia in italiano e inglese. In XAML:
    /// Text="{Binding [Chiave], Source={x:Static s:Loc.I}}". Cambiando lingua i binding si aggiornano da soli.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        public static Loc I { get; } = new();

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Lang { get; private set; } = "en";

        public void SetLanguage(string lang)
        {
            Lang = lang == "it" ? "it" : "en";
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Lang)));
        }

        public string this[string key] =>
            Strings.TryGetValue(key, out var pair) ? (Lang == "it" ? pair.It : pair.En) : key;

        public static string T(string key) => I[key];

        public static string F(string key, params object[] args) => string.Format(I[key], args);

        private static readonly Dictionary<string, (string It, string En)> Strings = new()
        {
            ["AppSubtitle"] = ("Traduttore offline", "Offline translator"),
            ["WindowTitle"] = ("DeepLocal, traduttore offline", "DeepLocal, offline translator"),

            // Testata
            ["ClipboardBtn"] = ("Traduci appunti", "Translate clipboard"),
            ["ClipboardTip"] = ("Traduce il testo copiato (Alt+T funziona da qualsiasi programma)", "Translates the copied text (Alt+T works from any app)"),
            ["ModelTip"] = ("Modello di traduzione", "Translation model"),
            ["SettingsTip"] = ("Impostazioni", "Settings"),

            // Barra lingue e pannelli
            ["SourceLangTip"] = ("Lingua di partenza", "Source language"),
            ["TargetLangTip"] = ("Lingua di arrivo", "Target language"),
            ["DetectedFmt"] = ("{0} (rilevata)", "{0} (detected)"),
            ["SwapTip"] = ("Inverti lingue e testi", "Swap languages and texts"),
            ["SourcePlaceholder"] = ("Scrivi o incolla il testo da tradurre", "Type or paste text to translate"),
            ["SourceHint"] = ("Alt+T traduce il testo copiato, da qualsiasi programma.", "Alt+T translates copied text, from any app."),
            ["TargetPlaceholder"] = ("Traduzione", "Translation"),
            ["ClearTip"] = ("Cancella il testo", "Clear text"),
            ["Translate"] = ("Traduci", "Translate"),
            ["TranslateTip"] = ("Traduci (Ctrl+Invio)", "Translate (Ctrl+Enter)"),
            ["Stop"] = ("Interrompi", "Stop"),
            ["StopTip"] = ("Interrompi la traduzione (Esc)", "Stop translating (Esc)"),
            ["Copy"] = ("Copia", "Copy"),
            ["CopyTip"] = ("Copia la traduzione", "Copy translation"),
            ["Copied"] = ("Copiata", "Copied"),
            ["CharsFmt"] = ("{0} caratteri", "{0} characters"),
            ["CharsOne"] = ("1 carattere", "1 character"),

            // Stato
            ["StatusReady"] = ("Pronto", "Ready"),
            ["StatusLoadingModels"] = ("Leggo i modelli di Ollama…", "Reading Ollama models…"),
            ["StatusDetecting"] = ("Rilevo la lingua…", "Detecting language…"),
            ["StatusTranslatingFmt"] = ("Traduco in {0} con {1}…", "Translating into {0} with {1}…"),
            ["StatusThinking"] = ("Il modello sta ragionando…", "The model is thinking…"),
            ["StatusDoneFmt"] = ("Tradotto in {0:0.0} s", "Translated in {0:0.0} s"),
            ["StatusStopped"] = ("Traduzione interrotta", "Translation stopped"),
            ["StatusCopied"] = ("Traduzione copiata negli appunti", "Translation copied to the clipboard"),
            ["StatusClipboardEmpty"] = ("Negli appunti non c'è testo da tradurre", "There is no text in the clipboard"),
            ["StatusSwapped"] = ("Lingue invertite", "Languages swapped"),
            ["StatusTargetSwitchedFmt"] = ("Il testo è già in {0}: traduco in {1}", "The text is already in {0}: translating into {1}"),
            ["OllamaOk"] = ("Ollama attivo", "Ollama running"),
            ["OllamaDown"] = ("Ollama non raggiungibile", "Ollama unreachable"),

            // Errori
            ["ErrOllamaDownFmt"] = ("Ollama non risponde su {0}. Avvialo e riprova.", "Ollama is not responding at {0}. Start it and try again."),
            ["ErrModelMissingFmt"] = ("Il modello {0} non è installato. Scaricalo dal menu dei modelli.", "Model {0} is not installed. Download it from the model menu."),
            ["ErrNoModel"] = ("Nessun modello pronto. Scarica TranslateGemma 12B dal menu dei modelli.", "No model ready. Download TranslateGemma 12B from the model menu."),
            ["ErrCloudFmt"] = ("{0} è un modello cloud: accedi con \"ollama signin\" e riprova.", "{0} is a cloud model: sign in with \"ollama signin\" and try again."),
            ["ErrGenericFmt"] = ("Errore: {0}", "Error: {0}"),
            ["ErrHotkey"] = ("Alt+T è già usato da un altro programma: usa il pulsante Traduci appunti.", "Alt+T is already used by another app: use the Translate clipboard button."),
            ["StartOllama"] = ("Avvia Ollama", "Start Ollama"),
            ["Retry"] = ("Riprova", "Retry"),

            // Menu modelli
            ["ModelsInstalled"] = ("Installati", "Installed"),
            ["ModelsCloud"] = ("Cloud (richiedono internet)", "Cloud (internet required)"),
            ["ModelsToDownload"] = ("Da scaricare", "Available to download"),
            ["Recommended"] = ("Consigliato", "Recommended"),
            ["CloudBadge"] = ("Cloud", "Cloud"),
            ["Download"] = ("Scarica", "Download"),
            ["DownloadSizeFmt"] = ("Scarica ({0})", "Download ({0})"),
            ["Cancel"] = ("Annulla", "Cancel"),
            ["DownloadingFmt"] = ("Scarico {0}… {1:0}%", "Downloading {0}… {1:0}%"),
            ["DownloadPreparing"] = ("Preparo il download…", "Preparing download…"),
            ["DownloadDoneFmt"] = ("{0} scaricato e pronto", "{0} downloaded and ready"),
            ["DownloadCancelled"] = ("Download annullato (riprende da dove si era fermato)", "Download cancelled (it will resume where it stopped)"),
            ["RefreshModels"] = ("Aggiorna elenco", "Refresh list"),
            ["FallbackModelFmt"] = ("{0} non è installato: uso {1}", "{0} is not installed: using {1}"),
            ["NoModelsYet"] = ("Nessun modello", "No model"),

            // Impostazioni
            ["Settings"] = ("Impostazioni", "Settings"),
            ["UiLanguage"] = ("Lingua dell'interfaccia", "Interface language"),
            ["AutoTranslate"] = ("Traduci mentre scrivo", "Translate as I type"),
            ["StartWithWindows"] = ("Avvia con Windows (nella tray)", "Start with Windows (in the tray)"),
            ["Theme"] = ("Il tema segue Windows (chiaro o scuro).", "The theme follows Windows (light or dark)."),
            ["OpenGitHub"] = ("Pagina del progetto su GitHub", "Project page on GitHub"),
            ["SupportLiberapay"] = ("Sostieni il progetto su Liberapay", "Support the project on Liberapay"),
            ["VersionFmt"] = ("Versione {0}", "Version {0}"),

            // Tray
            ["TrayOpen"] = ("Apri DeepLocal", "Open DeepLocal"),
            ["TrayHide"] = ("Nascondi finestra", "Hide window"),
            ["TrayClipboard"] = ("Traduci appunti (Alt+T)", "Translate clipboard (Alt+T)"),
            ["TrayExit"] = ("Esci", "Exit"),
            ["TrayText"] = ("DeepLocal, traduttore offline", "DeepLocal, offline translator"),
        };
    }
}
