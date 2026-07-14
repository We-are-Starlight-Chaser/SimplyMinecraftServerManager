// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using SimplyMinecraftServerManager.ViewModels.Pages;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
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
    public partial class ConsoleViewer : UserControl, IDisposable
    {
        private CancellationTokenSource? _searchCts;
        private readonly DispatcherTimer _searchDebounceTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
        private static readonly Regex[] TimestampPatterns =
        [
            new(@"^(?<prefix>\[\d{2}:\d{2}:\d{2}(?:\s+[^\]]+)?\]:?)(?<rest>\s*.*)$", RegexOptions.Compiled),
            new(@"^(?<prefix>\[\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?\]:?)(?<rest>\s*.*)$", RegexOptions.Compiled),
            new(@"^(?<prefix>\d{4}-\d{2}-\d{2}[ T]\d{2}:\d{2}:\d{2}(?:\.\d+)?)(?<rest>\s+.*)$", RegexOptions.Compiled),
            new(@"^(?<prefix>\d{2}:\d{2}:\d{2})(?<rest>\s+.*)$", RegexOptions.Compiled)
        ];
        private static readonly Regex AnsiRegex = new(@"\x1B\[(?<codes>[0-9;:]*)m", RegexOptions.Compiled);
        private static readonly Regex ChatMessageRegex = new(@"^(?<secure>\[Not Secure\]\s+)?<(?<name>[^>]+)>\s(?<message>.*)$", RegexOptions.Compiled);
        private static readonly Regex CommandMessageRegex = new(@"^(?<name>.+?)\sissued server command:\s*(?<command>/?.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex JoinLeaveRegex = new(@"^(?<name>.+?)\s(?<action>joined|left)\sthe game\b(?<rest>.*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Dictionary<char, Color> MinecraftLegacyColors = new()
        {
            ['0'] = Color.FromRgb(0x00, 0x00, 0x00),
            ['1'] = Color.FromRgb(0x00, 0x00, 0xAA),
            ['2'] = Color.FromRgb(0x00, 0xAA, 0x00),
            ['3'] = Color.FromRgb(0x00, 0xAA, 0xAA),
            ['4'] = Color.FromRgb(0xAA, 0x00, 0x00),
            ['5'] = Color.FromRgb(0xAA, 0x00, 0xAA),
            ['6'] = Color.FromRgb(0xFF, 0xAA, 0x00),
            ['7'] = Color.FromRgb(0xAA, 0xAA, 0xAA),
            ['8'] = Color.FromRgb(0x55, 0x55, 0x55),
            ['9'] = Color.FromRgb(0x55, 0x55, 0xFF),
            ['a'] = Color.FromRgb(0x55, 0xFF, 0x55),
            ['b'] = Color.FromRgb(0x55, 0xFF, 0xFF),
            ['c'] = Color.FromRgb(0xFF, 0x55, 0x55),
            ['d'] = Color.FromRgb(0xFF, 0x55, 0xFF),
            ['e'] = Color.FromRgb(0xFF, 0xFF, 0x55),
            ['f'] = Color.FromRgb(0xFF, 0xFF, 0xFF)
        };
private static readonly Dictionary<int, Color> AnsiBaseColors = new()
{
            [0] = Color.FromRgb(0x00, 0x00, 0x00),
            [1] = Color.FromRgb(0xCD, 0x31, 0x31),
            [2] = Color.FromRgb(0x0D, 0xBC, 0x79),
            [3] = Color.FromRgb(0xE5, 0xE5, 0x10),
            [4] = Color.FromRgb(0x24, 0x72, 0xC8),
            [5] = Color.FromRgb(0xBC, 0x3F, 0xBC),
            [6] = Color.FromRgb(0x11, 0xA8, 0xCD),
            [7] = Color.FromRgb(0xE5, 0xE5, 0xE5),
            [8] = Color.FromRgb(0x66, 0x66, 0x66),
            [9] = Color.FromRgb(0xF1, 0x4C, 0x4C),
            [10] = Color.FromRgb(0x23, 0xD1, 0x8B),
            [11] = Color.FromRgb(0xF5, 0xF5, 0x43),
            [12] = Color.FromRgb(0x3B, 0x8E, 0xFF),
            [13] = Color.FromRgb(0xD6, 0x70, 0xD6),
            [14] = Color.FromRgb(0x29, 0xB8, 0xDB),
            [15] = Color.FromRgb(0xFF, 0xFF, 0xFF)
        };

        private static readonly ConcurrentDictionary<Color, SolidColorBrush> _brushCache = new();
        private static SolidColorBrush? _timestampBrush;
        private static SolidColorBrush? _searchHighlightBrush;
        private static SolidColorBrush? _defaultForegroundBrush;
        private static SolidColorBrush? _defaultBackgroundBrush;
        private static SolidColorBrush? _whiteBrush;
        private static SolidColorBrush? _errorBrush;
        private static SolidColorBrush? _warningBrush;
        private static SolidColorBrush? _successBrush;
        private static SolidColorBrush? _debugBrush;
        private static SolidColorBrush? _startupBrush;
        private static SolidColorBrush? _chatNameBrush;
        private static SolidColorBrush? _commandBrush;
        private static SolidColorBrush? _joinBrush;
        private static SolidColorBrush? _leaveBrush;
        private static SolidColorBrush? _secureBrush;
        private static SolidColorBrush? _secondaryTextBrush;
        private static SolidColorBrush? _messageBrush;

        private InstanceViewModel? _subscribedViewModel;
        private readonly List<Run> _searchMatches = [];
        private int _currentSearchIndex = -1;
        private bool _isSearchOpen;
        private ScrollViewer? _scrollViewer;
        private bool _userScrolledUp;

        private readonly Stack<Paragraph> _paragraphPool = new(64);
        private readonly Stack<Run> _runPool = new(256);
        private int _paragraphPoolPeak;
        private int _runPoolPeak;
        private int _poolShrinkCounter;
        private readonly ConcurrentQueue<string> _pendingLines = new();
        private int _batchPending;
        private const int MaxLineLength = 8192;

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
            _searchCts?.Cancel();
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
            var oldBlocks = new List<Block>();
            while (ConsoleTextBox.Document.Blocks.Count > 0)
            {
                var block = ConsoleTextBox.Document.Blocks.FirstBlock!;
                oldBlocks.Add(block);
                ConsoleTextBox.Document.Blocks.Remove(block);
            }
            ConsoleTextBox.Document = CreateDocument();
            foreach (var block in oldBlocks)
                RecycleParagraph(block);
            _searchMatches.Clear();
            _currentSearchIndex = -1;

            var searchText = GetActiveSearchText();
            foreach (var line in lines)
            {
                ConsoleTextBox.Document.Blocks.Add(CreateParagraph(line, searchText));
            }

            ApplyConsoleAppearance();
            UpdateEmptyHintVisibility(lines.Count > 0);
            _userScrolledUp = false;
            UpdateSearchResultText();
            SelectCurrentSearchResult();
            QueueAutoScrollIfNeeded();
        }

        private Paragraph GetPooledParagraph()
        {
            return _paragraphPool.Count > 0 ? _paragraphPool.Pop() : new Paragraph { Margin = new Thickness(0) };
        }

        private Run GetPooledRun()
        {
            return _runPool.Count > 0 ? _runPool.Pop() : new Run();
        }

        private Paragraph CreateParagraph(string line, string searchText)
        {
            var paragraph = GetPooledParagraph();

            if (TrySplitTimestampPrefix(line, out var prefix, out var content))
            {
                paragraph.Inlines.Add(new Run(prefix)
                {
                    Foreground = CreateTimestampBrush()
                });

                if (!string.IsNullOrEmpty(content))
                {
                    AddHighlightedText(paragraph, GetStyledSegments(line, prefix, content.TrimStart()), searchText);
                }
            }
            else
            {
                AddHighlightedText(paragraph, GetStyledSegments(line, string.Empty, line), searchText);
            }

            return paragraph;
        }

        private List<StyledSegment> GetStyledSegments(string fullLine, string prefix, string content)
        {
            var segments = ParseStyledSegments(content);
            if (segments.Count == 0 || HasExplicitStyling(segments))
            {
                return segments;
            }

            var fallbackSegments = BuildSemanticSegments(fullLine, prefix, content);
            return fallbackSegments.Count > 0 ? fallbackSegments : segments;
        }

        private void AddHighlightedText(Paragraph paragraph, IReadOnlyList<StyledSegment> segments, string searchText)
        {
            if (segments.Count == 0)
            {
                return;
            }

            var sb = _parseBuffer ??= new StringBuilder(256);
            sb.Clear();
            foreach (var segment in segments)
                sb.Append(segment.Text);
            var visibleText = sb.ToString();
            if (visibleText.Length == 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(searchText))
            {
                foreach (var segment in segments)
                {
                    paragraph.Inlines.Add(CreateRun(segment.Text, segment, isSearchHit: false));
                }
                return;
            }

            var highlightRanges = FindSearchRanges(visibleText, searchText);
            if (highlightRanges.Count == 0)
            {
                foreach (var segment in segments)
                {
                    paragraph.Inlines.Add(CreateRun(segment.Text, segment, isSearchHit: false));
                }

                return;
            }

            var visibleOffset = 0;
            var activeRangeIndex = 0;
            foreach (var segment in segments)
            {
                var segmentText = segment.Text;
                var segmentStart = visibleOffset;
                var segmentEnd = visibleOffset + segmentText.Length;
                var cursor = 0;

                while (activeRangeIndex < highlightRanges.Count && highlightRanges[activeRangeIndex].End <= segmentStart)
                {
                    activeRangeIndex++;
                }

                var rangeIndex = activeRangeIndex;
                while (cursor < segmentText.Length)
                {
                    if (rangeIndex >= highlightRanges.Count || highlightRanges[rangeIndex].Start >= segmentEnd)
                    {
                        paragraph.Inlines.Add(CreateRun(segmentText[cursor..], segment, isSearchHit: false));
                        break;
                    }

                    var range = highlightRanges[rangeIndex];
                    var localHighlightStart = Math.Max(range.Start, segmentStart) - segmentStart;
                    var localHighlightEnd = Math.Min(range.End, segmentEnd) - segmentStart;

                    if (localHighlightStart > cursor)
                    {
                        paragraph.Inlines.Add(CreateRun(segmentText[cursor..localHighlightStart], segment, isSearchHit: false));
                    }

                    if (localHighlightEnd > localHighlightStart)
                    {
                        var highlightedRun = CreateRun(segmentText[localHighlightStart..localHighlightEnd], segment, isSearchHit: true);
                        paragraph.Inlines.Add(highlightedRun);
                        _searchMatches.Add(highlightedRun);
                    }

                    cursor = localHighlightEnd;
                    if (range.End <= segmentEnd)
                    {
                        rangeIndex++;
                    }
                }

                visibleOffset = segmentEnd;
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

private SolidColorBrush CreateTimestampBrush()
        {
            if (_timestampBrush != null) return _timestampBrush;
            
            var baseBrush = TryFindResource("TextFillColorSecondaryBrush") as Brush ?? Brushes.Gray;
            var brush = baseBrush.CloneCurrentValue();
            brush.Opacity = 0.65;
            brush.Freeze();
            _timestampBrush = (SolidColorBrush)brush;
            return _timestampBrush;
        }

        private SolidColorBrush CreateSearchHighlightBrush()
        {
            if (_searchHighlightBrush != null) return _searchHighlightBrush;
            
            var baseBrush = TryFindResource("AccentFillColorDefaultBrush") as Brush ?? Brushes.DodgerBlue;
            var brush = baseBrush.CloneCurrentValue();
            brush.Freeze();
            _searchHighlightBrush = (SolidColorBrush)brush;
            return _searchHighlightBrush;
        }

        private SolidColorBrush CreateDefaultConsoleForegroundBrush()
        {
            if (_defaultForegroundBrush != null) return _defaultForegroundBrush;
            
            var baseBrush = TryFindResource("TextFillColorPrimaryBrush") as Brush ?? Brushes.WhiteSmoke;
            var brush = baseBrush.CloneCurrentValue();
            brush.Freeze();
            _defaultForegroundBrush = (SolidColorBrush)brush;
            return _defaultForegroundBrush;
        }

        private SolidColorBrush CreateDefaultConsoleBackgroundBrush()
        {
            if (_defaultBackgroundBrush != null) return _defaultBackgroundBrush;
            
            var baseBrush = TryFindResource("ControlFillColorDefaultBrush") as Brush ?? Brushes.Black;
            var brush = baseBrush.CloneCurrentValue();
            brush.Freeze();
            _defaultBackgroundBrush = (SolidColorBrush)brush;
            return _defaultBackgroundBrush;
        }

        private static SolidColorBrush GetCachedBrush(Color color)
        {
            return _brushCache.GetOrAdd(color, c =>
            {
                var brush = new SolidColorBrush(c);
                brush.Freeze();
                return brush;
            });
        }

        private static SolidColorBrush WhiteBrush => _whiteBrush ??= GetCachedBrush(Colors.White);
        private static SolidColorBrush ErrorBrush => _errorBrush ??= GetCachedBrush(Color.FromRgb(0xFF, 0x66, 0x66));
        private static SolidColorBrush WarningBrush => _warningBrush ??= GetCachedBrush(Color.FromRgb(0xFF, 0xC1, 0x5A));
        private static SolidColorBrush SuccessBrush => _successBrush ??= GetCachedBrush(Color.FromRgb(0x67, 0xE0, 0x7C));
        private static SolidColorBrush DebugBrush => _debugBrush ??= GetCachedBrush(Color.FromRgb(0x79, 0xC9, 0xFF));
        private static SolidColorBrush StartupBrush => _startupBrush ??= GetCachedBrush(Color.FromRgb(0x6F, 0xE3, 0xC1));
        private static SolidColorBrush ChatNameBrush => _chatNameBrush ??= GetCachedBrush(Color.FromRgb(0x63, 0xD2, 0xFF));
        private static SolidColorBrush CommandBrush => _commandBrush ??= GetCachedBrush(Color.FromRgb(0x6F, 0xE3, 0xC1));
        private static SolidColorBrush JoinBrush => _joinBrush ??= GetCachedBrush(Color.FromRgb(0x67, 0xE0, 0x7C));
        private static SolidColorBrush LeaveBrush => _leaveBrush ??= GetCachedBrush(Color.FromRgb(0xFF, 0xC1, 0x5A));
        private static SolidColorBrush SecureBrush => _secureBrush ??= GetCachedBrush(Color.FromRgb(0xFF, 0xC1, 0x5A));
        private static SolidColorBrush SecondaryTextBrush => _secondaryTextBrush ??= GetCachedBrush(Color.FromRgb(0xB7, 0xB7, 0xB7));
private static SolidColorBrush MessageBrush => _messageBrush ??= GetCachedBrush(Color.FromRgb(0xF5, 0xF5, 0xF5));

        private Run CreateRun(string text, StyledSegment segment, bool isSearchHit)
        {
            var run = GetPooledRun();
            run.Text = text;
            var foreground = segment.Foreground;
            var background = segment.Background;

            if (segment.Inverse)
            {
                var originalForeground = foreground;
                var originalBackground = background;
                foreground = originalBackground ?? CreateDefaultConsoleBackgroundBrush();
                background = originalForeground ?? CreateDefaultConsoleForegroundBrush();
            }

            if (foreground != null)
            {
                run.Foreground = foreground;
            }

            if (background != null)
            {
                run.Background = background;
            }

            if (segment.IsBold)
            {
                run.FontWeight = FontWeights.Bold;
            }

            if (segment.IsItalic)
            {
                run.FontStyle = FontStyles.Italic;
            }

            if (segment.IsUnderline || segment.IsStrikethrough)
            {
                var textDecorations = new TextDecorationCollection();
                if (segment.IsUnderline)
                {
                    textDecorations.Add(TextDecorations.Underline[0]);
                }

                if (segment.IsStrikethrough)
                {
                    textDecorations.Add(TextDecorations.Strikethrough[0]);
                }

                run.TextDecorations = textDecorations;
            }

if (isSearchHit)
            {
                run.Background = CreateSearchHighlightBrush();
                run.Foreground = WhiteBrush;
            }

            return run;
        }

        private List<TextRangeInfo> FindSearchRanges(string text, string searchText)
        {
            var ranges = new List<TextRangeInfo>();
            var startIndex = 0;

            while (startIndex < text.Length)
            {
                var matchIndex = text.IndexOf(searchText, startIndex, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    break;
                }

                ranges.Add(new TextRangeInfo(matchIndex, matchIndex + searchText.Length));
                startIndex = matchIndex + searchText.Length;
            }

            return ranges;
        }

        [ThreadStatic]
        private static StringBuilder? _parseBuffer;

        private List<StyledSegment> ParseStyledSegments(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return [];
            }

            var segments = new List<StyledSegment>(8);
            var buffer = _parseBuffer ??= new StringBuilder(256);
            buffer.Clear();
            var style = ConsoleTextStyle.Default;
            var index = 0;

            void FlushBuffer()
            {
                if (buffer.Length == 0)
                {
                    return;
                }

                segments.Add(new StyledSegment(buffer.ToString(), style));
                buffer.Clear();
            }

            while (index < text.Length)
            {
                if (text[index] == '\u001b')
                {
                    var match = AnsiRegex.Match(text, index);
                    if (match.Success && match.Index == index)
                    {
                        FlushBuffer();
                        style = ApplyAnsiCodes(style, match.Groups["codes"].Value);
                        index += match.Length;
                        continue;
                    }
                }

                if (text[index] == '§' && index + 1 < text.Length)
                {
                    if (TryParseMinecraftHexColor(text, index, out var hexColor, out var consumedLength))
                    {
                        FlushBuffer();
                        style = style.WithMinecraftColor(hexColor);
                        index += consumedLength;
                        continue;
                    }

                    var code = char.ToLowerInvariant(text[index + 1]);
                    if (TryApplyMinecraftCode(code, style, out var updatedStyle))
                    {
                        FlushBuffer();
                        style = updatedStyle;
                        index += 2;
                        continue;
                    }
                }

                buffer.Append(text[index]);
                index++;
            }

            FlushBuffer();
            return segments;
        }

        private bool HasExplicitStyling(IReadOnlyList<StyledSegment> segments)
        {
            for (int i = 0; i < segments.Count; i++)
            {
                var segment = segments[i];
                if (segment.Foreground != null || segment.Background != null
                    || segment.IsBold || segment.IsItalic
                    || segment.IsUnderline || segment.IsStrikethrough
                    || segment.Inverse)
                    return true;
            }
            return false;
        }

        private List<StyledSegment> BuildSemanticSegments(string fullLine, string prefix, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return [];
            }

            if (fullLine.StartsWith("[ERR] ", StringComparison.Ordinal))
            {
return
                [
                    CreateStyledSegment(content, foreground: ErrorBrush, isBold: true)
                ];
            }

            var chatMatch = ChatMessageRegex.Match(content);
            if (chatMatch.Success)
            {
                var segments = new List<StyledSegment>();
                var secure = chatMatch.Groups["secure"].Value;
                if (!string.IsNullOrEmpty(secure))
                {
                    segments.Add(CreateStyledSegment(secure, foreground: SecureBrush));
                }

                segments.Add(CreateStyledSegment($"<{chatMatch.Groups["name"].Value}>", foreground: ChatNameBrush, isBold: true));
                segments.Add(CreateStyledSegment(" ", foreground: null));
                segments.Add(CreateStyledSegment(chatMatch.Groups["message"].Value, foreground: MessageBrush));
                return segments;
            }

            var commandMatch = CommandMessageRegex.Match(content);
            if (commandMatch.Success)
            {
                return
                [
                    CreateStyledSegment(commandMatch.Groups["name"].Value, foreground: ChatNameBrush, isBold: true),
                    CreateStyledSegment(" issued server command: ", foreground: SecondaryTextBrush),
                    CreateStyledSegment(commandMatch.Groups["command"].Value, foreground: CommandBrush)
                ];
            }

            var joinLeaveMatch = JoinLeaveRegex.Match(content);
            if (joinLeaveMatch.Success)
            {
                var action = joinLeaveMatch.Groups["action"].Value.ToLowerInvariant();
                var actionBrush = action == "joined" ? JoinBrush : LeaveBrush;

                return
                [
                    CreateStyledSegment(joinLeaveMatch.Groups["name"].Value, foreground: ChatNameBrush, isBold: true),
                    CreateStyledSegment($" {joinLeaveMatch.Groups["action"].Value} the game", foreground: actionBrush, isBold: true),
                    CreateStyledSegment(joinLeaveMatch.Groups["rest"].Value, foreground: actionBrush)
                ];
            }

            var severity = DetectSeverity(fullLine, prefix, content);
            if (severity == ConsoleSemanticSeverity.None)
            {
                if (content.StartsWith(": Done (", StringComparison.OrdinalIgnoreCase)
                    || content.StartsWith("Done (", StringComparison.OrdinalIgnoreCase))
                {
                    severity = ConsoleSemanticSeverity.Success;
                }
                else if (content.Contains("Starting minecraft server", StringComparison.OrdinalIgnoreCase)
                    || content.Contains("Preparing level", StringComparison.OrdinalIgnoreCase))
                {
                    severity = ConsoleSemanticSeverity.Startup;
                }
            }

            var severityBrush = severity switch
            {
                ConsoleSemanticSeverity.Debug => DebugBrush,
                ConsoleSemanticSeverity.Warning => WarningBrush,
                ConsoleSemanticSeverity.Error => ErrorBrush,
                ConsoleSemanticSeverity.Success => SuccessBrush,
                ConsoleSemanticSeverity.Startup => StartupBrush,
                _ => null
            };

            if (severityBrush == null)
            {
                return [];
            }

            return
            [
                CreateStyledSegment(content, foreground: severityBrush, isBold: severity is ConsoleSemanticSeverity.Warning or ConsoleSemanticSeverity.Error)
            ];
        }

        private ConsoleSemanticSeverity DetectSeverity(string fullLine, string prefix, string content)
        {
            static bool ContainsAny(string source, ReadOnlySpan<string> targets)
            {
                foreach (var target in targets)
                {
                    if (source.Contains(target, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            ReadOnlySpan<string> errorWords = ["FATAL", "SEVERE", "ERROR", "Exception", "Caused by:"];
            ReadOnlySpan<string> errorPrefix = ["FATAL", "SEVERE", "ERROR"];
            ReadOnlySpan<string> warnWords = ["WARN", "WARNING"];
            ReadOnlySpan<string> warnPrefix = ["WARN"];
            ReadOnlySpan<string> debugWords = ["DEBUG", "TRACE"];
            ReadOnlySpan<string> debugPrefix = ["DEBUG"];

            if (ContainsAny(content, errorWords) || ContainsAny(prefix, errorPrefix))
                return ConsoleSemanticSeverity.Error;

            if (ContainsAny(content, warnWords) || ContainsAny(prefix, warnPrefix))
                return ConsoleSemanticSeverity.Warning;

            if (ContainsAny(content, debugWords) || ContainsAny(prefix, debugPrefix))
                return ConsoleSemanticSeverity.Debug;

            if (content.Contains("INFO", StringComparison.OrdinalIgnoreCase) ||
                prefix.Contains("INFO", StringComparison.OrdinalIgnoreCase))
                return ConsoleSemanticSeverity.Info;

            return ConsoleSemanticSeverity.None;
        }

        private static StyledSegment CreateStyledSegment(
            string text,
            Brush? foreground = null,
            Brush? background = null,
            bool isBold = false,
            bool isItalic = false,
            bool isUnderline = false,
            bool isStrikethrough = false,
            bool inverse = false)
        {
            return new StyledSegment(
                text,
                new ConsoleTextStyle(foreground, background, isBold, isItalic, isUnderline, isStrikethrough, inverse));
        }

        private ConsoleTextStyle ApplyAnsiCodes(ConsoleTextStyle style, string codesText)
        {
            if (string.IsNullOrWhiteSpace(codesText))
            {
                return ConsoleTextStyle.Default;
            }

            var tokens = codesText.Replace(':', ';').Split(';', StringSplitOptions.TrimEntries);
            if (tokens.Length == 0)
            {
                return ConsoleTextStyle.Default;
            }

            for (var i = 0; i < tokens.Length; i++)
            {
                if (!int.TryParse(tokens[i], out var code))
                {
                    continue;
                }

                switch (code)
                {
                    case 0:
                        style = ConsoleTextStyle.Default;
                        break;
                    case 1:
                        style = style with { IsBold = true };
                        break;
                    case 3:
                        style = style with { IsItalic = true };
                        break;
                    case 4:
                        style = style with { IsUnderline = true };
                        break;
                    case 7:
                        style = style with { Inverse = true };
                        break;
                    case 9:
                        style = style with { IsStrikethrough = true };
                        break;
                    case 22:
                        style = style with { IsBold = false };
                        break;
                    case 23:
                        style = style with { IsItalic = false };
                        break;
                    case 24:
                        style = style with { IsUnderline = false };
                        break;
                    case 27:
                        style = style with { Inverse = false };
                        break;
                    case 29:
                        style = style with { IsStrikethrough = false };
                        break;
                    case 39:
                        style = style with { Foreground = null };
                        break;
                    case 49:
                        style = style with { Background = null };
                        break;
                    case >= 30 and <= 37:
                        style = style with { Foreground = CreateBrush(AnsiBaseColors[code - 30]) };
                        break;
                    case >= 40 and <= 47:
                        style = style with { Background = CreateBrush(AnsiBaseColors[code - 40]) };
                        break;
                    case >= 90 and <= 97:
                        style = style with { Foreground = CreateBrush(AnsiBaseColors[code - 90 + 8]) };
                        break;
                    case >= 100 and <= 107:
                        style = style with { Background = CreateBrush(AnsiBaseColors[code - 100 + 8]) };
                        break;
                    case 38:
                    case 48:
                    {
                        var isForeground = code == 38;
                        if (i + 1 >= tokens.Length || !int.TryParse(tokens[i + 1], out var mode))
                        {
                            break;
                        }

                        if (mode == 5 && i + 2 < tokens.Length && int.TryParse(tokens[i + 2], out var paletteIndex))
                        {
                            var brush = CreateBrush(MapAnsiPaletteColor(paletteIndex));
                            style = isForeground
                                ? style with { Foreground = brush }
                                : style with { Background = brush };
                            i += 2;
                        }
                        else if (mode == 2
                            && i + 4 < tokens.Length
                            && int.TryParse(tokens[i + 2], out var red)
                            && int.TryParse(tokens[i + 3], out var green)
                            && int.TryParse(tokens[i + 4], out var blue))
                        {
                            var brush = CreateBrush(Color.FromRgb((byte)Math.Clamp(red, 0, 255), (byte)Math.Clamp(green, 0, 255), (byte)Math.Clamp(blue, 0, 255)));
                            style = isForeground
                                ? style with { Foreground = brush }
                                : style with { Background = brush };
                            i += 4;
                        }

                        break;
                    }
                }
            }

            return style;
        }

        private static bool TryApplyMinecraftCode(char code, ConsoleTextStyle currentStyle, out ConsoleTextStyle updatedStyle)
        {
            if (MinecraftLegacyColors.TryGetValue(code, out var color))
            {
                updatedStyle = currentStyle.WithMinecraftColor(color);
                return true;
            }

            updatedStyle = code switch
            {
                'l' => currentStyle with { IsBold = true },
                'm' => currentStyle with { IsStrikethrough = true },
                'n' => currentStyle with { IsUnderline = true },
                'o' => currentStyle with { IsItalic = true },
                'r' => ConsoleTextStyle.Default,
                _ => currentStyle
            };

            return code is 'l' or 'm' or 'n' or 'o' or 'r' or 'k';
        }

        private static bool TryParseMinecraftHexColor(ReadOnlySpan<char> text, int startIndex, out Color color, out int consumedLength)
        {
            color = default;
            consumedLength = 0;

            if (startIndex + 13 >= text.Length || char.ToLowerInvariant(text[startIndex + 1]) != 'x')
            {
                return false;
            }

            Span<char> hexChars = stackalloc char[6];
            for (var i = 0; i < 6; i++)
            {
                var markerIndex = startIndex + 2 + (i * 2);
                if (text[markerIndex] != '\u00a7')
                {
                    return false;
                }

                var hexChar = text[markerIndex + 1];
                if (!Uri.IsHexDigit(hexChar))
                {
                    return false;
                }

                hexChars[i] = hexChar;
            }

            color = Color.FromRgb(
                (byte)((HexValue(hexChars[0]) << 4) | HexValue(hexChars[1])),
                (byte)((HexValue(hexChars[2]) << 4) | HexValue(hexChars[3])),
                (byte)((HexValue(hexChars[4]) << 4) | HexValue(hexChars[5])));
            consumedLength = 14;
            return true;

            static int HexValue(char c) => c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'f' => c - 'a' + 10,
                >= 'A' and <= 'F' => c - 'A' + 10,
                _ => 0
            };
        }

        private static SolidColorBrush CreateBrush(Color color)
        {
            return GetCachedBrush(color);
        }

        private static Color MapAnsiPaletteColor(int paletteIndex)
        {
            var clamped = Math.Clamp(paletteIndex, 0, 255);
            if (clamped < 16)
            {
                return AnsiBaseColors[clamped];
            }

            if (clamped < 232)
            {
                clamped -= 16;
                var red = clamped / 36;
                var green = (clamped % 36) / 6;
                var blue = clamped % 6;

                static byte MapCubeComponent(int component) => component == 0 ? (byte)0 : (byte)(55 + (component * 40));

                return Color.FromRgb(MapCubeComponent(red), MapCubeComponent(green), MapCubeComponent(blue));
            }

            var gray = (byte)(8 + ((clamped - 232) * 10));
            return Color.FromRgb(gray, gray, gray);
        }

        private static string TruncateLine(string line)
        {
            return line.Length <= MaxLineLength ? line : line[..MaxLineLength];
        }

        private void OnConsoleLineAdded(object? sender, string line)
        {
            _pendingLines.Enqueue(TruncateLine(line));
            if (Interlocked.CompareExchange(ref _batchPending, 1, 0) == 0)
            {
                Dispatcher.BeginInvoke(ProcessBatch, DispatcherPriority.Background);
            }
        }

        private void ProcessBatch()
        {
            if (_pendingLines.IsEmpty)
            {
                Interlocked.Exchange(ref _batchPending, 0);
                if (!_pendingLines.IsEmpty && Interlocked.CompareExchange(ref _batchPending, 1, 0) == 0)
                    Dispatcher.BeginInvoke(ProcessBatch, DispatcherPriority.Background);
                return;
            }

            var lines = new List<string>();
            while (_pendingLines.TryDequeue(out var line))
                lines.Add(line);
            Interlocked.Exchange(ref _batchPending, 0);

            if (!_pendingLines.IsEmpty && Interlocked.CompareExchange(ref _batchPending, 1, 0) == 0)
                Dispatcher.BeginInvoke(ProcessBatch, DispatcherPriority.Background);

            if (lines.Count == 0) return;

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(() => ProcessBatchInternal(lines), DispatcherPriority.Background);
                return;
            }
            ProcessBatchInternal(lines);
        }

        private void ProcessBatchInternal(List<string> lines)
        {
            var document = ConsoleTextBox.Document;
            var searchText = GetActiveSearchText();
            bool hasSearch = !string.IsNullOrWhiteSpace(searchText);

            var paragraphs = new List<Paragraph>(lines.Count);
            foreach (var line in lines)
            {
                paragraphs.Add(hasSearch
                    ? CreateParagraph(line, searchText)
                    : CreateParagraph(line, string.Empty));
            }

            foreach (var para in paragraphs)
                document.Blocks.Add(para);

            var maxBlocks = InstanceViewModel.MaxConsoleLines;
            if (document.Blocks.Count > maxBlocks)
            {
                int toRemove = document.Blocks.Count - maxBlocks;
                var blocksToRemove = new Block[toRemove];
                var current = document.Blocks.FirstBlock;
                for (int i = 0; i < toRemove && current != null; i++)
                {
                    blocksToRemove[i] = current;
                    current = current.NextBlock;
                }
                foreach (var block in blocksToRemove)
                {
                    document.Blocks.Remove(block);
                    RecycleParagraph(block);
                }
            }

            UpdateEmptyHintVisibility(true);
            if (hasSearch) UpdateSearchResultText();
            QueueAutoScrollIfNeeded();
        }

        private void RecycleParagraph(Block block)
        {
            if (block is Paragraph p)
            {
                var inlines = p.Inlines.ToArray();
                foreach (var inline in inlines)
                {
                    if (inline is Run r) { r.Text = ""; _runPool.Push(r); }
                }
                p.Inlines.Clear();
                _paragraphPool.Push(p);

                if (_paragraphPool.Count > _paragraphPoolPeak)
                    _paragraphPoolPeak = _paragraphPool.Count;
                if (_runPool.Count > _runPoolPeak)
                    _runPoolPeak = _runPool.Count;

                if (++_poolShrinkCounter >= 256)
                {
                    _poolShrinkCounter = 0;
                    ShrinkPools();
                }
            }
        }

        private void ShrinkPools()
        {
            var targetParagraphs = Math.Max(32, (int)(_paragraphPoolPeak * 1.5));
            while (_paragraphPool.Count > targetParagraphs)
                _paragraphPool.Pop();

            var targetRuns = Math.Max(128, (int)(_runPoolPeak * 1.5));
            while (_runPool.Count > targetRuns)
                _runPool.Pop();

            _paragraphPoolPeak = Math.Max(_paragraphPool.Count, 32);
            _runPoolPeak = Math.Max(_runPool.Count, 128);
        }

        private void OnConsoleCleared(object? sender, EventArgs e)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.InvokeAsync(() => OnConsoleCleared(sender, e));
                return;
            }

            var oldBlocks = new List<Block>();
            while (ConsoleTextBox.Document.Blocks.Count > 0)
            {
                var block = ConsoleTextBox.Document.Blocks.FirstBlock!;
                oldBlocks.Add(block);
                ConsoleTextBox.Document.Blocks.Remove(block);
            }
            ConsoleTextBox.Document = CreateDocument();
            foreach (var block in oldBlocks)
                RecycleParagraph(block);
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

            if (_userScrolledUp) return;

            _ = Dispatcher.InvokeAsync(() =>
            {
                ConsoleTextBox.CaretPosition = ConsoleTextBox.Document.ContentEnd;
                ConsoleTextBox.ScrollToEnd();
                _scrollViewer ??= FindDescendant<ScrollViewer>(ConsoleTextBox);
                _scrollViewer?.ScrollToBottom();
            }, DispatcherPriority.Background);
        }

        private void AttachScrollViewer()
        {
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = FindDescendant<ScrollViewer>(ConsoleTextBox);
            if (_scrollViewer != null)
                _scrollViewer.ScrollChanged += OnScrollChanged;
        }

        private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer == null) return;
            _userScrolledUp = _scrollViewer.VerticalOffset < _scrollViewer.ScrollableHeight - 16;
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

        private async void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isSearchOpen) return;
            var oldCts = _searchCts;
            _searchCts = new CancellationTokenSource();
            var token = _searchCts.Token;
            oldCts?.Cancel();
            oldCts?.Dispose();

            try
            {
                var lineCount = ViewModel?.GetConsoleLines().Count ?? 0;
                var delay = lineCount switch
                {
                    < 500 => 100,
                    < 2000 => 250,
                    < 5000 => 500,
                    _ => 800
                };
                await Task.Delay(delay, token);
                if (!token.IsCancellationRequested && Dispatcher.CheckAccess())
                {
                    RebuildDocument();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ConsoleViewer] Search rebuild failed: {ex.Message}");
            }
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

        private sealed record TextRangeInfo(int Start, int End);

        private sealed record StyledSegment(string Text, ConsoleTextStyle Style)
        {
            public Brush? Foreground => Style.Foreground;
            public Brush? Background => Style.Background;
            public bool IsBold => Style.IsBold;
            public bool IsItalic => Style.IsItalic;
            public bool IsUnderline => Style.IsUnderline;
            public bool IsStrikethrough => Style.IsStrikethrough;
            public bool Inverse => Style.Inverse;
        }

        private sealed record ConsoleTextStyle(
            Brush? Foreground,
            Brush? Background,
            bool IsBold,
            bool IsItalic,
            bool IsUnderline,
            bool IsStrikethrough,
            bool Inverse)
        {
            public static ConsoleTextStyle Default { get; } = new(null, null, false, false, false, false, false);

            public ConsoleTextStyle WithMinecraftColor(Color color) => this with
            {
                Foreground = CreateBrush(color),
                Background = null,
                IsBold = false,
                IsItalic = false,
                IsUnderline = false,
                IsStrikethrough = false,
                Inverse = false
            };
        }

        private enum ConsoleSemanticSeverity
        {
            None,
            Debug,
            Info,
            Warning,
            Error,
            Success,
            Startup
        }

        public void Dispose()
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            _searchCts = null;

            _searchDebounceTimer.Stop();

            if (_subscribedViewModel != null)
            {
                _subscribedViewModel.ConsoleLineAdded -= OnConsoleLineAdded;
                _subscribedViewModel = null;
            }

            GC.SuppressFinalize(this);
        }
    }
}
