using System.ComponentModel;

namespace DeepLocal.Services
{
    public enum ModelGroup { Installed, Cloud, ToDownload }

    /// <summary>Una riga del menu modelli: installato, cloud o da scaricare (con avanzamento).</summary>
    public sealed class ModelItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public ModelItem(string name) { Name = name; }

        public string Name { get; }
        public long Size { get; init; }
        public string Family { get; init; } = "";
        public string ParameterSize { get; init; } = "";
        public bool IsCloud { get; init; }
        public bool CanThink { get; init; }
        public bool IsRecommended { get; init; }

        /// <summary>Dimensione nota del download quando il modello non è ancora installato.</summary>
        public string DownloadSize { get; init; } = "";

        private bool _isInstalled;
        public bool IsInstalled { get => _isInstalled; set => Set(ref _isInstalled, value, nameof(IsInstalled), nameof(Group), nameof(Detail)); }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value, nameof(IsSelected)); }

        private bool _isDownloading;
        public bool IsDownloading { get => _isDownloading; set => Set(ref _isDownloading, value, nameof(IsDownloading), nameof(Detail)); }

        private double _progress;
        public double Progress { get => _progress; set => Set(ref _progress, value, nameof(Progress), nameof(Detail)); }

        private string _progressText = "";
        public string ProgressText { get => _progressText; set => Set(ref _progressText, value, nameof(ProgressText), nameof(Detail)); }

        public ModelGroup Group => !IsInstalled ? ModelGroup.ToDownload : IsCloud ? ModelGroup.Cloud : ModelGroup.Installed;

        /// <summary>Riga secondaria: "8.1 GB · gemma3 · 12.2B", "Cloud", oppure l'avanzamento del download.</summary>
        public string Detail
        {
            get
            {
                if (IsDownloading) return ProgressText;
                if (!IsInstalled) return DownloadSize;
                if (IsCloud) return Loc.T("CloudBadge");
                var parts = new[] { FormatSize(Size), Family, ParameterSize };
                return string.Join(" · ", System.Array.FindAll(parts, p => !string.IsNullOrEmpty(p)));
            }
        }

        public void RefreshTexts() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Detail)));

        public static string FormatSize(long bytes) =>
            bytes <= 0 ? "" : bytes >= 1_000_000_000 ? $"{bytes / 1e9:0.0} GB" : $"{bytes / 1e6:0} MB";

        private void Set<T>(ref T field, T value, params string[] names)
        {
            if (Equals(field, value)) return;
            field = value;
            foreach (var n in names) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
}
