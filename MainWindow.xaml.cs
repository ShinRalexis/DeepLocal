using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DeepLocal.Services;
using WpfControls = System.Windows.Controls;

namespace DeepLocal
{
    public partial class MainWindow : Window
    {
        private readonly Settings _settings = Settings.Load();
        private readonly OllamaClient _ollama = new();

        // Menu modelli, diviso in tre gruppi
        private readonly ObservableCollection<ModelItem> _installed = new();
        private readonly ObservableCollection<ModelItem> _cloud = new();
        private readonly ObservableCollection<ModelItem> _toDownload = new();
        private readonly Dictionary<string, CancellationTokenSource> _downloads = new();
        private ModelItem? _current;
        private bool _ollamaUp;

        // Lingue
        private readonly List<LangOption> _sourceOptions;
        private readonly List<LangOption> _targetOptions;
        private Language? _detected;
        private string? _detectedFor;
        private Language _lastExplicitSource = Languages.ByCode("en")!;

        // Traduzione in corso e digitazione
        private CancellationTokenSource? _translateCts;
        private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(900) };
        private readonly DispatcherTimer _copiedReset = new() { Interval = TimeSpan.FromMilliseconds(1600) };
        private bool _suppressTextEvents;
        private bool _suppressLangEvents;
        private bool _loadingSettingsUi;

        private enum ErrorKind { None, StartOllama, Retry, OpenModels }
        private ErrorKind _errorKind = ErrorKind.None;

        // Hotkey globale Alt+T
        private const int HOTKEY_ID = 9000;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_NOREPEAT = 0x4000;
        private const int WM_HOTKEY = 0x0312;
        private const int WM_DPICHANGED = 0x02E0;

        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _source;
        private bool _hotkeyOk;

        public MainWindow()
        {
            InitializeComponent();
            Loc.I.SetLanguage(_settings.Ui);

            InstalledList.ItemsSource = _installed;
            CloudList.ItemsSource = _cloud;
            DownloadList.ItemsSource = _toDownload;

            _sourceOptions = new[] { Languages.Auto }.Concat(Languages.All).Select(l => new LangOption(l)).ToList();
            _targetOptions = Languages.All.Select(l => new LangOption(l)).ToList();
            RefreshLanguageTexts();

            _suppressLangEvents = true;
            SourceLang.SelectedItem = _sourceOptions.First(o => o.Lang.Code == _settings.SourceLang);
            TargetLang.SelectedItem = _targetOptions.First(o => o.Lang.Code == _settings.TargetLang);
            _suppressLangEvents = false;
            if (SelectedSource is { IsAuto: false } src) _lastExplicitSource = src;
            else if (SelectedTarget.Code != "en") _lastExplicitSource = Languages.ByCode("en")!;
            else _lastExplicitSource = Languages.ByCode("it")!;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = Loc.F("VersionFmt", version == null ? "2.0" : $"{version.Major}.{version.Minor}.{version.Build}");

            _debounce.Tick += async (_, _) =>
            {
                _debounce.Stop();
                await TranslateAsync();
            };
            _copiedReset.Tick += (_, _) =>
            {
                _copiedReset.Stop();
                CopyIcon.Text = "\uE8C8";
                CopyText.Text = Loc.T("Copy");
            };
            TargetBox.TextChanged += (_, _) => UpdateTargetUi();

            UpdateSourceUi();
            UpdateTargetUi();
            ApplyFlow();
            SetStatus(Loc.T("StatusLoadingModels"));

            Loaded += async (_, _) => await RefreshModelsAsync();

            // Quando torna visibile (dalla tray o con Alt+T) si riposiziona in basso a destra
            IsVisibleChanged += (_, _) =>
            {
                if (IsVisible)
                {
                    App.PlaceWindowBottomRight(this);
                    App.ResnapAfterFirstLayout(this);
                }
            };
        }

        // ============================================================
        //  Finestra, tray e hotkey
        // ============================================================

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            ThemeManager.ApplyTitleBar(this);

            var helper = new WindowInteropHelper(this);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(HwndHook);
            _hotkeyOk = RegisterHotKey(helper.Handle, HOTKEY_ID, MOD_ALT | MOD_NOREPEAT, (uint)KeyInterop.VirtualKeyFromKey(Key.T));
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_source != null)
            {
                _source.RemoveHook(HwndHook);
                UnregisterHotKey(_source.Handle, HOTKEY_ID);
            }
            foreach (var cts in _downloads.Values) cts.Cancel();
            base.OnClosed(e);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                _ = TranslateClipboardAsync();
                handled = true;
            }
            else if (msg == WM_DPICHANGED)
            {
                // Cambio di monitor o di scaling: si riposiziona subito
                Dispatcher.BeginInvoke(() => App.PlaceWindowBottomRight(this), DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState == WindowState.Minimized)
            {
                // Riduci a icona = nella tray. Si ripristina lo stato normale subito,
                // così Alt+T o la tray la riaprono visibile e non ridotta.
                Hide();
                WindowState = WindowState.Normal;
                App.Instance?.UpdateMenuItems();
            }
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            base.OnClosing(e);
            if (!App.AllowClose)
            {
                // La X manda nella tray; si esce dal menu della tray
                e.Cancel = true;
                ModelPopup.IsOpen = false;
                SettingsPopup.IsOpen = false;
                Hide();
                App.Instance?.UpdateMenuItems();
            }
        }

        /// <summary>Mostra la finestra in primo piano, anche se era nascosta nella tray.</summary>
        public void ShowAndActivate()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
            // Porta davvero in primo piano anche se un'altra app ha il focus
            Topmost = true;
            Topmost = false;
            Focus();
            App.Instance?.UpdateMenuItems();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                _ = TranslateAsync();
            }
            else if (e.Key == Key.Escape)
            {
                if (ModelPopup.IsOpen || SettingsPopup.IsOpen) return;
                if (_translateCts != null)
                {
                    e.Handled = true;
                    StopTranslation();
                }
            }
        }

        // ============================================================
        //  Modelli
        // ============================================================

        private async Task RefreshModelsAsync(bool quiet = false)
        {
            _ollamaUp = await _ollama.PingAsync();
            UpdateOllamaState();
            if (!_ollamaUp)
            {
                ModelName.Text = _settings.Model;
                ShowError(Loc.F("ErrOllamaDownFmt", _ollama.Host),
                          OllamaClient.FindOllamaApp() != null ? ErrorKind.StartOllama : ErrorKind.Retry);
                SetStatus(Loc.T("OllamaDown"));
                UpdateModelSections();
                return;
            }

            List<OllamaModel> models;
            try
            {
                models = await _ollama.ListModelsAsync();
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("ErrGenericFmt", ex.Message));
                return;
            }

            if (_errorKind is ErrorKind.StartOllama or ErrorKind.Retry) HideError();

            var usable = models.Where(m => m.CanGenerate).ToList();
            var items = usable.Select(m => new ModelItem(m.Name)
            {
                Size = m.Size,
                Family = m.Family,
                ParameterSize = m.ParameterSize,
                IsCloud = m.IsCloud,
                CanThink = m.CanThink,
                IsRecommended = string.Equals(m.Name, Settings.DefaultModel, StringComparison.OrdinalIgnoreCase),
                IsInstalled = true,
            }).ToList();

            _installed.Clear();
            foreach (var m in items.Where(i => !i.IsCloud).OrderByDescending(i => i.IsRecommended).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                _installed.Add(m);
            _cloud.Clear();
            foreach (var m in items.Where(i => i.IsCloud).OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                _cloud.Add(m);

            // TranslateGemma 12B da scaricare se manca (i download in corso restano quelli di prima)
            var keep = _toDownload.Where(d => d.IsDownloading).ToList();
            _toDownload.Clear();
            foreach (var k in keep) _toDownload.Add(k);
            bool hasDefault = items.Any(i => i.IsRecommended);
            if (!hasDefault && !keep.Any(k => k.IsRecommended))
            {
                _toDownload.Add(new ModelItem(Settings.DefaultModel)
                {
                    IsRecommended = true,
                    Family = "gemma3",
                    DownloadSize = "TranslateGemma 12B · 8.1 GB",
                });
            }

            // Scelta del modello: quello salvato, poi TranslateGemma, poi il primo locale, poi il primo cloud
            var all = _installed.Concat(_cloud).ToList();
            var wanted = all.FirstOrDefault(i => string.Equals(i.Name, _settings.Model, StringComparison.OrdinalIgnoreCase));
            var pick = wanted
                       ?? all.FirstOrDefault(i => i.IsRecommended)
                       ?? _installed.FirstOrDefault()
                       ?? _cloud.FirstOrDefault();

            SelectModel(pick, save: false);

            if (pick == null)
            {
                ShowError(Loc.T("ErrNoModel"), ErrorKind.OpenModels);
                SetStatus(Loc.T("NoModelsYet"));
            }
            else if (wanted == null && !quiet)
            {
                SetStatus(Loc.F("FallbackModelFmt", _settings.Model, pick.Name));
            }
            else
            {
                if (_errorKind == ErrorKind.OpenModels) HideError();
                if (!quiet) SetStatus(Loc.T("StatusReady"));
            }

            UpdateModelSections();
        }

        private void SelectModel(ModelItem? item, bool save)
        {
            _current = item;
            foreach (var m in _installed.Concat(_cloud)) m.IsSelected = ReferenceEquals(m, item);
            ModelName.Text = item?.Name ?? Loc.T("NoModelsYet");
            if (item != null && save)
            {
                _settings.Model = item.Name;
                _settings.Save();
            }
        }

        private void UpdateModelSections()
        {
            InstalledHeader.Visibility = _installed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            CloudHeader.Visibility = _cloud.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            DownloadHeader.Visibility = _toDownload.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateOllamaState()
        {
            var key = _ollamaUp ? "Success" : "Danger";
            OllamaDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
            StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, key);
            OllamaStatus.Text = (_ollamaUp ? Loc.T("OllamaOk") : Loc.T("OllamaDown")) + "  ·  " + _ollama.Host.Replace("http://", "");
        }

        private async void ModelPopup_Opened(object sender, EventArgs e)
        {
            // Elenco sempre aggiornato: i modelli scaricati da terminale compaiono senza riavviare
            if (_downloads.Count == 0) await RefreshModelsAsync(quiet: true);
        }

        private async void ModelRow_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ModelItem item) return;
            ModelToggle.IsChecked = false;
            if (ReferenceEquals(item, _current)) return;
            SelectModel(item, save: true);
            _detectedFor = null; // il rilevamento dipende dal modello
            if (_errorKind == ErrorKind.OpenModels) HideError();
            if (_settings.AutoTranslate && !string.IsNullOrWhiteSpace(SourceBox.Text)) await TranslateAsync();
        }

        private async void RefreshModels_Click(object sender, RoutedEventArgs e) => await RefreshModelsAsync();

        private async void DownloadModel_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not ModelItem item || item.IsDownloading) return;
            if (!_ollamaUp)
            {
                ModelToggle.IsChecked = false;
                ShowError(Loc.F("ErrOllamaDownFmt", _ollama.Host),
                          OllamaClient.FindOllamaApp() != null ? ErrorKind.StartOllama : ErrorKind.Retry);
                return;
            }

            var cts = new CancellationTokenSource();
            _downloads[item.Name] = cts;
            item.IsDownloading = true;
            item.Progress = 0;
            item.ProgressText = Loc.T("DownloadPreparing");
            SetStatus(Loc.T("DownloadPreparing"));

            // Ollama scarica per strati: la percentuale è la somma di tutti gli strati visti finora
            var layers = new Dictionary<string, (long Total, long Done)>();
            bool ok = false;
            try
            {
                await foreach (var p in _ollama.PullAsync(item.Name, cts.Token))
                {
                    if (p.Total > 0)
                    {
                        layers[p.Status] = (p.Total, p.Completed);
                        long total = layers.Values.Sum(l => l.Total), done = layers.Values.Sum(l => l.Done);
                        item.Progress = total > 0 ? done * 100.0 / total : 0;
                        var sizes = $"({ModelItem.FormatSize(done)} / {ModelItem.FormatSize(total)})";
                        item.ProgressText = $"{item.Progress:0}%  {sizes}";
                        SetStatus(Loc.F("DownloadingFmt", item.Name, item.Progress) + "  " + sizes);
                    }
                    if (p.Status == "success") ok = true;
                }
            }
            catch (OperationCanceledException)
            {
                SetStatus(Loc.T("DownloadCancelled"));
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("ErrGenericFmt", ex.Message));
            }
            finally
            {
                _downloads.Remove(item.Name);
                cts.Dispose();
                item.IsDownloading = false;
                item.ProgressText = "";
            }

            if (!ok) return;

            SetStatus(Loc.F("DownloadDoneFmt", item.Name));
            var previous = _current?.Name;
            await RefreshModelsAsync();
            // Il modello consigliato appena scaricato diventa quello in uso
            var fresh = _installed.FirstOrDefault(i => i.Name == item.Name);
            if (fresh != null && (item.IsRecommended || previous == null))
            {
                SelectModel(fresh, save: true);
                SetStatus(Loc.F("DownloadDoneFmt", item.Name));
            }
        }

        private void CancelDownload_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is ModelItem item && _downloads.TryGetValue(item.Name, out var cts))
                cts.Cancel();
        }

        // ============================================================
        //  Lingue
        // ============================================================

        private Language SelectedSource => (SourceLang.SelectedItem as LangOption)?.Lang ?? Languages.Auto;
        private Language SelectedTarget => (TargetLang.SelectedItem as LangOption)?.Lang ?? Languages.ByCode("en")!;

        /// <summary>Lingua effettiva della sorgente: quella scelta, o quella rilevata se "Rileva lingua".</summary>
        private Language? EffectiveSource => SelectedSource.IsAuto ? _detected : SelectedSource;

        private void RefreshLanguageTexts()
        {
            var ui = Loc.I.Lang;
            foreach (var o in _sourceOptions.Concat(_targetOptions))
                o.Display = o.Lang.IsAuto ? AutoLabel() : o.Lang.DisplayName(ui);

            // Ordine alfabetico nella lingua dell'interfaccia; "Rileva lingua" sempre per primo
            var selS = SourceLang.SelectedItem;
            var selT = TargetLang.SelectedItem;
            _suppressLangEvents = true;
            SourceLang.ItemsSource = _sourceOptions.OrderBy(o => o.Lang.IsAuto ? 0 : 1).ThenBy(o => o.Display, StringComparer.CurrentCulture).ToList();
            TargetLang.ItemsSource = _targetOptions.OrderBy(o => o.Display, StringComparer.CurrentCulture).ToList();
            SourceLang.SelectedItem = selS;
            TargetLang.SelectedItem = selT;
            _suppressLangEvents = false;
        }

        private string AutoLabel()
        {
            var name = Loc.I.Lang == "it" ? "Rileva lingua" : "Detect language";
            return _detected != null && SelectedSourceIsAutoSafe()
                ? Loc.F("DetectedFmt", _detected.DisplayName(Loc.I.Lang))
                : name;
        }

        private bool SelectedSourceIsAutoSafe() => SourceLang?.SelectedItem is not LangOption o || o.Lang.IsAuto;

        private void UpdateAutoLabel()
        {
            var auto = _sourceOptions.First(o => o.Lang.IsAuto);
            auto.Display = AutoLabel();
        }

        private void SelectLang(WpfControls.ComboBox combo, Language lang)
        {
            _suppressLangEvents = true;
            combo.SelectedItem = ((IEnumerable<LangOption>)combo.ItemsSource).First(o => o.Lang.Code == lang.Code);
            _suppressLangEvents = false;
        }

        private async void SourceLang_SelectionChanged(object sender, WpfControls.SelectionChangedEventArgs e)
        {
            if (_suppressLangEvents || !IsLoaded) return;
            var src = SelectedSource;
            if (!src.IsAuto)
            {
                _lastExplicitSource = src;
                // Stessa lingua di arrivo: si scambiano, come fa DeepL
                if (src.Code == SelectedTarget.Code && e.RemovedItems.Count > 0 && e.RemovedItems[0] is LangOption old)
                    SelectLang(TargetLang, old.Lang.IsAuto ? (_detected ?? AlternativeTarget(src)) : old.Lang);
            }
            UpdateAutoLabel();
            ApplyFlow();
            SaveLanguages();
            await RetranslateIfNeeded();
        }

        private async void TargetLang_SelectionChanged(object sender, WpfControls.SelectionChangedEventArgs e)
        {
            if (_suppressLangEvents || !IsLoaded) return;
            var tgt = SelectedTarget;
            if (SelectedSource is { IsAuto: false } src && src.Code == tgt.Code &&
                e.RemovedItems.Count > 0 && e.RemovedItems[0] is LangOption old)
            {
                SelectLang(SourceLang, old.Lang);
                _lastExplicitSource = old.Lang;
            }
            ApplyFlow();
            SaveLanguages();
            await RetranslateIfNeeded();
        }

        private void SaveLanguages()
        {
            _settings.SourceLang = SelectedSource.Code;
            _settings.TargetLang = SelectedTarget.Code;
            _settings.Save();
        }

        private async Task RetranslateIfNeeded()
        {
            if (_settings.AutoTranslate && !string.IsNullOrWhiteSpace(SourceBox.Text))
                await TranslateAsync();
        }

        /// <summary>Lingua di arrivo alternativa quando il testo è già nella lingua di arrivo.</summary>
        private Language AlternativeTarget(Language source)
        {
            var ui = Languages.ByCode(Loc.I.Lang)!;
            if (source.Code != ui.Code) return ui;
            return source.Code == "en" ? Languages.ByCode("it")! : Languages.ByCode("en")!;
        }

        private void ApplyFlow()
        {
            SourceBox.FlowDirection = EffectiveSource?.IsRtl == true ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            TargetBox.FlowDirection = SelectedTarget.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        }

        private void Swap_Click(object sender, RoutedEventArgs e)
        {
            StopTranslation(silent: true);
            var oldSource = EffectiveSource ?? _lastExplicitSource;
            var oldTarget = SelectedTarget;
            if (oldSource.Code == oldTarget.Code) oldSource = AlternativeTarget(oldTarget);

            SelectLang(SourceLang, oldTarget);
            SelectLang(TargetLang, oldSource);
            _lastExplicitSource = oldTarget;
            _detected = null;
            _detectedFor = null;
            UpdateAutoLabel();

            // Si scambiano anche i testi: la traduzione diventa il nuovo testo di partenza
            var translated = TargetBox.Text;
            if (!string.IsNullOrWhiteSpace(translated))
            {
                var original = SourceBox.Text;
                SetSourceText(translated);
                TargetBox.Text = original;
            }

            ApplyFlow();
            SaveLanguages();
            HideError();
            SetStatus(Loc.T("StatusSwapped"));
        }

        // ============================================================
        //  Testo e traduzione
        // ============================================================

        private void SetSourceText(string text)
        {
            _suppressTextEvents = true;
            SourceBox.Text = text;
            _suppressTextEvents = false;
            UpdateSourceUi();
        }

        private void SourceBox_TextChanged(object sender, WpfControls.TextChangedEventArgs e)
        {
            UpdateSourceUi();
            if (_suppressTextEvents) return;
            _debounce.Stop();
            if (string.IsNullOrWhiteSpace(SourceBox.Text))
            {
                StopTranslation(silent: true);
                TargetBox.Clear();
                _detected = null;
                _detectedFor = null;
                UpdateAutoLabel();
                if (_errorKind == ErrorKind.None) SetStatus(Loc.T("StatusReady"));
                return;
            }
            if (_settings.AutoTranslate) _debounce.Start();
        }

        private void UpdateSourceUi()
        {
            var empty = SourceBox.Text.Length == 0;
            SourcePlaceholder.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            ClearBtn.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            var n = SourceBox.Text.Length;
            CharCount.Text = n == 0 ? "" : n == 1 ? Loc.T("CharsOne") : Loc.F("CharsFmt", n.ToString("N0"));
        }

        private void UpdateTargetUi()
        {
            var empty = TargetBox.Text.Length == 0;
            TargetPlaceholder.Visibility = empty && ErrorPanel.Visibility != Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            CopyBtn.IsEnabled = !empty;
        }

        private async void TranslateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_translateCts != null) StopTranslation();
            else await TranslateAsync();
        }

        private async void ClipboardBtn_Click(object sender, RoutedEventArgs e) => await TranslateClipboardAsync();

        public async Task TranslateClipboardAsync()
        {
            string? text = null;
            for (int i = 0; i < 3 && text == null; i++)
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText()) text = System.Windows.Clipboard.GetText();
                    else break;
                }
                catch (COMException)
                {
                    await Task.Delay(60); // appunti occupati da un altro programma
                }
            }

            ShowAndActivate();
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus(Loc.T("StatusClipboardEmpty"));
                return;
            }

            SetSourceText(text);
            await TranslateAsync();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            StopTranslation(silent: true);
            SetSourceText("");
            TargetBox.Clear();
            _detected = null;
            _detectedFor = null;
            UpdateAutoLabel();
            ApplyFlow();
            if (_errorKind != ErrorKind.StartOllama) HideError();
            SetStatus(Loc.T("StatusReady"));
            SourceBox.Focus();
        }

        private void CopyTranslated_Click(object sender, RoutedEventArgs e)
        {
            var text = TargetBox.SelectionLength > 0 ? TargetBox.SelectedText : TargetBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    System.Windows.Clipboard.SetText(text);
                    CopyIcon.Text = "\uE73E";
                    CopyText.Text = Loc.T("Copied");
                    _copiedReset.Stop();
                    _copiedReset.Start();
                    SetStatus(Loc.T("StatusCopied"));
                    return;
                }
                catch (COMException)
                {
                    Thread.Sleep(50);
                }
            }
        }

        private void StopTranslation(bool silent = false)
        {
            _debounce.Stop();
            var cts = _translateCts;
            if (cts == null) return;
            _translateCts = null;
            cts.Cancel();
            SetBusy(false);
            if (!silent) SetStatus(Loc.T("StatusStopped"));
        }

        private async Task TranslateAsync()
        {
            _debounce.Stop();
            StopTranslation(silent: true);

            var text = SourceBox.Text;
            if (string.IsNullOrWhiteSpace(text))
            {
                TargetBox.Clear();
                return;
            }
            if (_current == null)
            {
                ShowError(_ollamaUp ? Loc.T("ErrNoModel") : Loc.F("ErrOllamaDownFmt", _ollama.Host),
                          _ollamaUp ? ErrorKind.OpenModels : OllamaClient.FindOllamaApp() != null ? ErrorKind.StartOllama : ErrorKind.Retry);
                return;
            }

            var model = _current;
            var cts = new CancellationTokenSource();
            _translateCts = cts;
            var ct = cts.Token;
            HideError();
            SetBusy(true);
            var sw = Stopwatch.StartNew();

            try
            {
                // 1. Lingua di partenza (rilevata se "Rileva lingua"; riusata mentre si continua a scrivere)
                Language? source = SelectedSource;
                if (source.IsAuto)
                {
                    if (!CanReuseDetection(text))
                    {
                        SetStatus(Loc.T("StatusDetecting"));
                        _detected = await Translator.DetectAsync(_ollama, model.Name, model.CanThink, text, ct);
                        _detectedFor = text;
                        UpdateAutoLabel();
                    }
                    source = _detected;
                }

                // 2. Testo già nella lingua di arrivo: si cambia la lingua di arrivo
                var target = SelectedTarget;
                if (source != null && source.Code == target.Code)
                {
                    var alt = AlternativeTarget(source);
                    SelectLang(TargetLang, alt);
                    SaveLanguages();
                    SetStatus(Loc.F("StatusTargetSwitchedFmt", source.DisplayName(Loc.I.Lang), alt.DisplayName(Loc.I.Lang)));
                    target = alt;
                }
                ApplyFlow();

                // 3. Traduzione in streaming
                SetStatus(Loc.F("StatusTranslatingFmt", target.DisplayName(Loc.I.Lang), model.Name));
                var prompt = Translator.BuildPrompt(model.Name, text, source, target);
                var sb = new StringBuilder();
                TargetBox.Clear();
                TargetInfo.Text = "";

                await foreach (var chunk in _ollama.GenerateAsync(model.Name, prompt, model.CanThink, ct))
                {
                    if (chunk.IsThinking)
                    {
                        SetStatus(Loc.T("StatusThinking"));
                        continue;
                    }
                    if (sb.Length == 0) SetStatus(Loc.F("StatusTranslatingFmt", target.DisplayName(Loc.I.Lang), model.Name));
                    sb.Append(chunk.Response);
                    if (TargetBox.Text.Length == 0) TargetBox.Text = chunk.Response.TrimStart();
                    else TargetBox.AppendText(chunk.Response);
                }

                ct.ThrowIfCancellationRequested();
                TargetBox.Text = Translator.CleanOutput(sb.ToString(), text);
                SetStatus(Loc.F("StatusDoneFmt", sw.Elapsed.TotalSeconds));
                TargetInfo.Text = model.Name;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Interrotta dall'utente o sostituita da una traduzione più recente
            }
            catch (OllamaException ex) when (ex.IsModelMissing)
            {
                ShowError(Loc.F("ErrModelMissingFmt", model.Name), ErrorKind.OpenModels);
                _ = RefreshModelsAsync();
            }
            catch (OllamaException ex) when (model.IsCloud && ex.IsUnauthorized)
            {
                ShowError(Loc.F("ErrCloudFmt", model.Name), ErrorKind.OpenModels);
            }
            catch (OllamaException ex)
            {
                ShowError(Loc.F("ErrGenericFmt", ex.Message), ErrorKind.Retry);
            }
            catch (HttpRequestException)
            {
                _ollamaUp = false;
                UpdateOllamaState();
                ShowError(Loc.F("ErrOllamaDownFmt", _ollama.Host),
                          OllamaClient.FindOllamaApp() != null ? ErrorKind.StartOllama : ErrorKind.Retry);
            }
            catch (Exception ex)
            {
                ShowError(Loc.F("ErrGenericFmt", ex.Message), ErrorKind.Retry);
            }
            finally
            {
                if (ReferenceEquals(_translateCts, cts))
                {
                    _translateCts = null;
                    SetBusy(false);
                }
                cts.Dispose();
            }
        }

        /// <summary>Mentre si scrive, la lingua rilevata sull'inizio del testo resta valida.</summary>
        private bool CanReuseDetection(string text)
        {
            if (_detectedFor == null || _detected == null) return false;
            if (_detectedFor == text) return true;
            const int prefix = 40;
            return _detectedFor.Length >= 24 && text.Length >= 24 &&
                   string.CompareOrdinal(_detectedFor, 0, text, 0, Math.Min(prefix, Math.Min(_detectedFor.Length, text.Length))) == 0;
        }

        // ============================================================
        //  Stato, errori, attività
        // ============================================================

        private void SetStatus(string text) => StatusText.Text = text;

        private void ShowError(string message, ErrorKind kind)
        {
            _errorKind = kind;
            ErrorText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
            ErrorAction.Visibility = kind == ErrorKind.None ? Visibility.Collapsed : Visibility.Visible;
            ErrorAction.Content = kind switch
            {
                ErrorKind.StartOllama => Loc.T("StartOllama"),
                ErrorKind.OpenModels => Loc.T("ModelTip"),
                _ => Loc.T("Retry"),
            };
            SetStatus(message);
            UpdateTargetUi();
        }

        private void HideError()
        {
            _errorKind = ErrorKind.None;
            ErrorPanel.Visibility = Visibility.Collapsed;
            UpdateTargetUi();
        }

        private async void ErrorAction_Click(object sender, RoutedEventArgs e)
        {
            switch (_errorKind)
            {
                case ErrorKind.OpenModels:
                    ModelToggle.IsChecked = true;
                    break;

                case ErrorKind.StartOllama:
                    var app = OllamaClient.FindOllamaApp();
                    if (app != null)
                    {
                        try { Process.Start(new ProcessStartInfo(app) { UseShellExecute = true }); }
                        catch (Exception ex) { ShowError(Loc.F("ErrGenericFmt", ex.Message), ErrorKind.Retry); return; }
                    }
                    ErrorAction.IsEnabled = false;
                    SetStatus(Loc.T("StatusLoadingModels"));
                    // Ollama impiega qualche secondo ad avviarsi
                    for (int i = 0; i < 20 && !await _ollama.PingAsync(); i++)
                        await Task.Delay(500);
                    ErrorAction.IsEnabled = true;
                    await RefreshModelsAsync();
                    if (_ollamaUp) await RetranslateIfNeeded();
                    break;

                case ErrorKind.Retry:
                    await RefreshModelsAsync();
                    if (_ollamaUp && _current != null) await TranslateAsync();
                    break;
            }
        }

        private void SetBusy(bool busy)
        {
            TranslateBtnText.Text = busy ? Loc.T("Stop") : Loc.T("Translate");
            TranslateBtn.ToolTip = busy ? Loc.T("StopTip") : Loc.T("TranslateTip");
            BusyLine.Visibility = busy ? Visibility.Visible : Visibility.Hidden;
            if (busy) StartBusyAnimation();
            else BusyShift.BeginAnimation(TranslateTransform.XProperty, null);
        }

        private void StartBusyAnimation()
        {
            var width = Math.Max(BusyLine.ActualWidth, 200);
            if (!SystemParameters.ClientAreaAnimation)
            {
                // Animazioni di Windows disattivate: barra ferma
                BusyShift.BeginAnimation(TranslateTransform.XProperty, null);
                BusyBar.Width = width;
                BusyShift.X = 0;
                return;
            }
            BusyBar.Width = 140;
            var anim = new DoubleAnimation(-140, width, TimeSpan.FromMilliseconds(1100))
            {
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseInOut },
            };
            BusyShift.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void BusyLine_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (BusyLine.Visibility == Visibility.Visible) StartBusyAnimation();
        }

        // ============================================================
        //  Impostazioni
        // ============================================================

        private void SettingsPopup_Opened(object sender, EventArgs e)
        {
            _loadingSettingsUi = true;
            UiItalian.IsChecked = Loc.I.Lang == "it";
            UiEnglish.IsChecked = Loc.I.Lang == "en";
            AutoTranslateSwitch.IsChecked = _settings.AutoTranslate;
            StartupSwitch.IsChecked = Settings.StartWithWindows;
            _loadingSettingsUi = false;
        }

        private void UiLanguage_Checked(object sender, RoutedEventArgs e)
        {
            if (_loadingSettingsUi || (sender as FrameworkElement)?.Tag is not string lang || lang == Loc.I.Lang) return;
            _settings.UiLanguage = lang;
            _settings.Ui = lang;
            _settings.Save();
            Loc.I.SetLanguage(lang);

            RefreshLanguageTexts();
            foreach (var m in _installed.Concat(_cloud).Concat(_toDownload)) m.RefreshTexts();
            UpdateSourceUi();
            SetBusy(_translateCts != null);
            CopyText.Text = Loc.T("Copy");
            if (_current == null) ModelName.Text = Loc.T("NoModelsYet");
            UpdateOllamaState();
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText.Text = Loc.F("VersionFmt", version == null ? "2.0" : $"{version.Major}.{version.Minor}.{version.Build}");
            SetStatus(Loc.T("StatusReady"));
            App.Instance?.RefreshTrayTexts();
        }

        private void AutoTranslate_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettingsUi) return;
            _settings.AutoTranslate = AutoTranslateSwitch.IsChecked == true;
            _settings.Save();
        }

        private void Startup_Changed(object sender, RoutedEventArgs e)
        {
            if (_loadingSettingsUi) return;
            Settings.StartWithWindows = StartupSwitch.IsChecked == true;
        }

        private void OpenLink_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string url) return;
            SettingsToggle.IsChecked = false;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                SetStatus(Loc.F("ErrGenericFmt", ex.Message));
            }
        }

        /// <summary>Messaggio all'avvio se Alt+T è occupato da un altro programma.</summary>
        public void ReportHotkeyIfFailed()
        {
            if (!_hotkeyOk) SetStatus(Loc.T("ErrHotkey"));
        }
    }

    /// <summary>Voce dei menu lingua: il testo mostrato cambia con la lingua dell'interfaccia e con il rilevamento.</summary>
    public sealed class LangOption : INotifyPropertyChanged
    {
        public LangOption(Language lang) { Lang = lang; }

        public Language Lang { get; }

        private string _display = "";
        public string Display
        {
            get => _display;
            set
            {
                if (_display == value) return;
                _display = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Display)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public override string ToString() => Display;
    }
}
