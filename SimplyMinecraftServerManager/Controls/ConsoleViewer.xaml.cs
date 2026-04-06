using SimplyMinecraftServerManager.ViewModels.Pages;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace SimplyMinecraftServerManager.Controls
{
    public partial class ConsoleViewer : UserControl
    {
        private static readonly Regex[] TimestampPatterns =
        [
            new(@"^(?<prefix>\[\d{2}:\d{2}:\d{2}(?:\s+[^\]]+)?\])(?<rest>\s*.*)$", RegexOptions.Compiled),
            new(@"^(?<prefix>\[\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?\])(?<rest>\s*.*)$", RegexOptions.Compiled),
            new(@"^(?<prefix>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?)(?<rest>\s+.*)$", RegexOptions.Compiled),
            new(@"^(?<prefix>\d{2}:\d{2}:\d{2})(?<rest>\s+.*)$", RegexOptions.Compiled)
        ];

        private InstanceViewModel? _subscribedViewModel;
        private readonly List<Run> _searchMatches = [];
        private int _currentSearchIndex = -1;
        private bool _isSearchOpen;
        private ScrollViewer? _scrollViewer;

        public static readonly DependencyProperty ViewModelProperty =
            DependencyProperty.Register(
                nameof(ViewModel),
                typeof(InstanceViewModel),
                typeof(ConsoleViewer),
                new PropertyMetadata(null, OnViewModelChanged));

        public static readonly DependencyProperty IsFullScreenProperty =
            DependencyProperty.Register(
                nameof(IsFullScreen),
                typeof(bool),
                typeof(ConsoleViewer),
                new PropertyMetadata(false));

        public InstanceViewModel? ViewModel
        {
            get => (InstanceViewModel?)GetValue(ViewModelProperty);
            set => SetValue(ViewModelProperty, value);
        }

        public bool IsFullScreen
        {
            get => (bool)GetValue(IsFullScreenProperty);
            set => SetValue(IsFullScreenProperty, value);
        }

        public ConsoleViewer()
        {
            InitializeComponent();
            ConsoleTextBox.Document = CreateDocument();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            SizeChanged += OnViewerSizeChanged;
        }

        private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ConsoleViewer viewer)
            {
                return;
            }

            viewer.SubscribeToViewModel(e.NewValue as InstanceViewModel);
            if (viewer.IsLoaded)
            {
                viewer.InitializeConsole();
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            SubscribeToViewModel(ViewModel);
            _ = Dispatcher.InvokeAsync(AttachScrollViewer, DispatcherPriority.Loaded);
            InitializeConsole();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            SubscribeToViewModel(null);
        }

        private void OnViewerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyConsoleAppearance();
            if (_scrollViewer == null)
            {
                AttachScrollViewer();
            }
        }

        private void SubscribeToViewModel(InstanceViewModel? viewModel)
        {
            if (ReferenceEquals(_subscribedViewModel, viewModel))
            {
                return;
            }

            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.ConsoleLineAdded -= OnConsoleLineAdded;
                _subscribedViewModel.ConsoleCleared -= OnConsoleCleared;
                _subscribedViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            _subscribedViewModel = viewModel;

            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.ConsoleLineAdded += OnConsoleLineAdded;
                _subscribedViewModel.ConsoleCleared += OnConsoleCleared;
                _subscribedViewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => OnViewModelPropertyChanged(sender, e));
                return;
            }

            if (e.PropertyName is nameof(InstanceViewModel.ConsoleWrapLines)
                or nameof(InstanceViewModel.ConsoleFontFamily)
                or nameof(InstanceViewModel.ConsoleFontSize))
            {
                ApplyConsoleAppearance();
            }

            if (e.PropertyName is nameof(InstanceViewModel.IsRunning))
            {
                UpdateEmptyHintVisibility(ConsoleTextBox.Document.Blocks.Count > 0);
            }

            if (e.PropertyName is nameof(InstanceViewModel.IsConsoleFullScreen)
                && IsFullScreen
                && ViewModel?.IsConsoleFullScreen == false
                && _isSearchOpen)
            {
                SetSearchPanelVisibility(false);
            }
        }

        private void InitializeConsole()
        {
            RebuildDocument();
            ApplyConsoleAppearance();
        }

        private FlowDocument CreateDocument()
        {
            return new FlowDocument
            {
                PagePadding = new Thickness(12, 10, 12, 10)
            };
        }

        private void ApplyConsoleAppearance()
        {
            var document = ConsoleTextBox.Document;
            document.FontFamily = ConsoleTextBox.FontFamily;
            document.FontSize = ConsoleTextBox.FontSize;

            if (ViewModel?.ConsoleWrapLines == true)
            {
                document.PageWidth = Math.Max(0, ConsoleTextBox.ActualWidth - 36);
            }
            else
            {
                document.PageWidth = 100000;
            }
        }

        private void RebuildDocument()
        {
            var lines = ViewModel?.GetConsoleLines() ?? [];
            var document = CreateDocument();
            ConsoleTextBox.Document = document;
            _searchMatches.Clear();
            _currentSearchIndex = -1;

            var searchText = GetActiveSearchText();
            foreach (var line in lines)
            {
                document.Blocks.Add(CreateParagraph(line, searchText));
            }

            ApplyConsoleAppearance();
            UpdateEmptyHintVisibility(lines.Count > 0);
            UpdateSearchResultText();
            SelectCurrentSearchResult();
            QueueAutoScrollIfNeeded();
        }

        private Paragraph CreateParagraph(string line, string searchText)
        {
            var paragraph = new Paragraph
            {
                Margin = new Thickness(0)
            };

            if (TrySplitTimestampPrefix(line, out var prefix, out var content))
            {
                paragraph.Inlines.Add(new Run(prefix)
                {
                    Foreground = CreateTimestampBrush()
                });

                if (!string.IsNullOrEmpty(content))
                {
                    AddHighlightedText(paragraph, content.TrimStart(), searchText);
                }
            }
            else
            {
                AddHighlightedText(paragraph, line, searchText);
            }

            return paragraph;
        }

        private void AddHighlightedText(Paragraph paragraph, string text, string searchText)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                paragraph.Inlines.Add(new Run(text));
                return;
            }

            var startIndex = 0;
            while (startIndex < text.Length)
            {
                var matchIndex = text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    paragraph.Inlines.Add(new Run(text[startIndex..]));
                    break;
                }

                if (matchIndex > startIndex)
                {
                    paragraph.Inlines.Add(new Run(text[startIndex..matchIndex]));
                }

                var matchRun = new Run(text.Substring(matchIndex, searchText.Length))
                {
                    Background = CreateSearchHighlightBrush(),
                    Foreground = Brushes.White
                };

                paragraph.Inlines.Add(matchRun);
                _searchMatches.Add(matchRun);
                startIndex = matchIndex + searchText.Length;
            }
        }

        private bool TrySplitTimestampPrefix(string line, out string prefix, out string content)
        {
            foreach (var pattern in TimestampPatterns)
            {
                var match = pattern.Match(line);
                if (match.Success)
                {
                    prefix = match.Groups["prefix"].Value;
                    content = match.Groups["rest"].Value;
                    return true;
                }
            }

            prefix = string.Empty;
            content = line;
            return false;
        }

        private Brush CreateTimestampBrush()
        {
            var baseBrush = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
            var brush = baseBrush.CloneCurrentValue();
            brush.Opacity = 0.65;
            return brush;
        }

        private Brush CreateSearchHighlightBrush()
        {
            var baseBrush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
            return baseBrush.CloneCurrentValue();
        }

        private void OnConsoleLineAdded(object? sender, string line)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => OnConsoleLineAdded(sender, line));
                return;
            }

            if (!string.IsNullOrWhiteSpace(GetActiveSearchText()))
            {
                RebuildDocument();
                return;
            }

            ConsoleTextBox.Document.Blocks.Add(CreateParagraph(line, string.Empty));
            while (ConsoleTextBox.Document.Blocks.Count > InstanceViewModel.MaxConsoleLines)
            {
                ConsoleTextBox.Document.Blocks.Remove(ConsoleTextBox.Document.Blocks.FirstBlock);
            }

            UpdateEmptyHintVisibility(true);
            QueueAutoScrollIfNeeded();
        }

        private void OnConsoleCleared(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => OnConsoleCleared(sender, e));
                return;
            }

            ConsoleTextBox.Document = CreateDocument();
            _searchMatches.Clear();
            _currentSearchIndex = -1;
            ApplyConsoleAppearance();
            UpdateEmptyHintVisibility(false);
            UpdateSearchResultText();
        }

        private void UpdateEmptyHintVisibility(bool hasContent)
        {
            ConsoleEmptyHint.Visibility = hasContent || ViewModel?.IsRunning == true
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        private void QueueAutoScrollIfNeeded()
        {
            if (ViewModel?.AutoScroll != true || !string.IsNullOrWhiteSpace(GetActiveSearchText()))
            {
                return;
            }

            _ = Dispatcher.InvokeAsync(() =>
            {
                ConsoleTextBox.CaretPosition = ConsoleTextBox.Document.ContentEnd;
                ConsoleTextBox.ScrollToEnd();
                ConsoleTextBox.UpdateLayout();
                _scrollViewer ??= FindDescendant<ScrollViewer>(ConsoleTextBox);
                _scrollViewer?.ScrollToBottom();
            }, DispatcherPriority.Background);
        }

        private void AttachScrollViewer()
        {
            _scrollViewer = FindDescendant<ScrollViewer>(ConsoleTextBox);
        }

        private static T? FindDescendant<T>(DependencyObject? parent)
            where T : DependencyObject
        {
            if (parent == null)
            {
                return null;
            }

            var childrenCount = GetChildrenCount(parent);
            for (var index = 0; index < childrenCount; index++)
            {
                var child = GetChild(parent, index);
                if (child is T match)
                {
                    return match;
                }

                var descendant = FindDescendant<T>(child);
                if (descendant != null)
                {
                    return descendant;
                }
            }

            return null;
        }

        private static int GetChildrenCount(DependencyObject parent)
        {
            return parent switch
            {
                Visual or Visual3D => VisualTreeHelper.GetChildrenCount(parent),
                _ => LogicalTreeHelper.GetChildren(parent).OfType<object>().Count()
            };
        }

        private static DependencyObject? GetChild(DependencyObject parent, int index)
        {
            return parent switch
            {
                Visual or Visual3D => VisualTreeHelper.GetChild(parent, index),
                _ => LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>().ElementAtOrDefault(index)
            };
        }

        private string GetActiveSearchText()
        {
            return _isSearchOpen ? SearchTextBox.Text.Trim() : string.Empty;
        }

        private void UpdateSearchResultText()
        {
            if (!_isSearchOpen)
            {
                SearchResultText.Text = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(SearchTextBox.Text))
            {
                SearchResultText.Text = "输入关键词开始搜索";
                return;
            }

            if (_searchMatches.Count == 0)
            {
                SearchResultText.Text = "无结果";
                return;
            }

            if (_currentSearchIndex < 0)
            {
                _currentSearchIndex = 0;
            }

            SearchResultText.Text = $"{_currentSearchIndex + 1} / {_searchMatches.Count}";
        }

        private void SelectCurrentSearchResult()
        {
            if (_searchMatches.Count == 0)
            {
                ConsoleTextBox.Selection.Select(ConsoleTextBox.Document.ContentEnd, ConsoleTextBox.Document.ContentEnd);
                return;
            }

            if (_currentSearchIndex < 0 || _currentSearchIndex >= _searchMatches.Count)
            {
                _currentSearchIndex = 0;
            }

            var currentMatch = _searchMatches[_currentSearchIndex];
            ConsoleTextBox.Selection.Select(currentMatch.ContentStart, currentMatch.ContentEnd);
            currentMatch.BringIntoView();
            UpdateSearchResultText();
        }

        private void NavigateSearch(int delta)
        {
            if (_searchMatches.Count == 0)
            {
                return;
            }

            _currentSearchIndex = (_currentSearchIndex + delta + _searchMatches.Count) % _searchMatches.Count;
            SelectCurrentSearchResult();
        }

        private void SetSearchPanelVisibility(bool isVisible)
        {
            if (!IsFullScreen)
            {
                return;
            }

            _isSearchOpen = isVisible;
            SearchPanel.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;

            if (!isVisible)
            {
                SearchTextBox.Text = string.Empty;
                RebuildDocument();
                return;
            }

            _ = Dispatcher.InvokeAsync(() => SearchTextBox.Focus(), DispatcherPriority.Background);
            RebuildDocument();
        }

        private void OnToggleSearchClick(object sender, RoutedEventArgs e)
        {
            SetSearchPanelVisibility(!_isSearchOpen);
        }

        private void OnCloseSearchClick(object sender, RoutedEventArgs e)
        {
            SetSearchPanelVisibility(false);
        }

        private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSearchOpen)
            {
                return;
            }

            RebuildDocument();
        }

        private void OnSearchTextBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                SetSearchPanelVisibility(false);
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                NavigateSearch(1);
                e.Handled = true;
            }
        }

        private void OnPreviousSearchResultClick(object sender, RoutedEventArgs e)
        {
            NavigateSearch(-1);
        }

        private void OnNextSearchResultClick(object sender, RoutedEventArgs e)
        {
            NavigateSearch(1);
        }

        private void OnOpenFullScreenClick(object sender, RoutedEventArgs e)
        {
            if (ViewModel == null)
            {
                return;
            }

            if (IsFullScreen)
            {
                return;
            }

            ViewModel.EnterConsoleFullScreenCommand.Execute(null);

            var owner = Window.GetWindow(this);
            var window = new Views.Windows.ConsoleFullScreenWindow(ViewModel)
            {
                Owner = owner
            };

            window.Show();
        }

        private void OnCloseFullScreenClick(object sender, RoutedEventArgs e)
        {
            if (_isSearchOpen)
            {
                SetSearchPanelVisibility(false);
            }

            ViewModel?.ExitConsoleFullScreenCommand.Execute(null);
        }

        private void OnCommandKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.None)
            {
                ViewModel?.SendCommandCommand.Execute(null);
                e.Handled = true;
            }
        }

        private void OnConsoleTextChanged(object sender, TextChangedEventArgs e)
        {
            QueueAutoScrollIfNeeded();
        }
    }
}
