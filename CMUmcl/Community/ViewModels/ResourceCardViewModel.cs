// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;

namespace CMUmcl.Community
{
    internal sealed class ResourceCardViewModel : ObservableObject
    {
        private readonly ResourceProject _project;
        public ResourceCardViewModel(ResourceProject project, CompatibilityHint hint, bool showCompatibility=true)
        {
            _project = project;
            ShowCompatibility=showCompatibility;
            Hint = hint;
        }

        public ResourceProject Project => _project;
        public bool ShowCompatibility {get;}
        public string Title => _project.Title;
        public string Description => _project.Description;
        public string? IconUrl => _project.IconUrl;
        public string Author => string.IsNullOrWhiteSpace(_project.Author) ? "" : _project.Author!;
        public string DownloadsText => _project.Downloads < 0 ? "" : _project.Downloads.ToString("N0");
        public string UpdatedText => _project.UpdatedAt?.ToString("yyyy-MM-dd") ?? "";
        public string SourceLabel => _project.SourceLabel;
        public IReadOnlyList<string> LoaderChips => _project.Loaders;
        public IReadOnlyList<string> VersionChips => _project.GameVersions;

        private CompatibilityHint _hint;
        public CompatibilityHint Hint { get => _hint; set { if (Set(ref _hint, value)) OnPropertyChanged(nameof(HintText)); } }
        public string HintText => !ShowCompatibility?"":_hint switch
        {
            CompatibilityHint.Compatible => "兼容",
            CompatibilityHint.Incompatible => "不兼容",
            _ => "兼容性未知",
        };
        public bool IsCompatible => _hint == CompatibilityHint.Compatible;
        public bool IsIncompatible => _hint == CompatibilityHint.Incompatible;

        private BitmapSource? _icon;
        public BitmapSource? Icon => _icon;
        public void SetIcon(BitmapSource? icon) { if (Set(ref _icon, icon)) OnPropertyChanged(nameof(Icon)); }
    }
}
