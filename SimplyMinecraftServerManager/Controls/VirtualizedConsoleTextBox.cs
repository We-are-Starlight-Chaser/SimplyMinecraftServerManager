using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SimplyMinecraftServerManager.Controls
{
    public class VirtualizedConsoleTextBox : RichTextBox
    {
        private readonly Queue<Paragraph> _paragraphPool = new();
        private readonly Lock _lock = new();
        private int _maxVisibleParagraphs = 1000;
        private Paragraph? _currentParagraph;
        private bool _isUpdating;

        public static readonly DependencyProperty MaxLinesProperty =
            DependencyProperty.Register(nameof(MaxLines), typeof(int), typeof(VirtualizedConsoleTextBox),
                new PropertyMetadata(1000, OnMaxLinesChanged));

        public int MaxLines
        {
            get => (int)GetValue(MaxLinesProperty);
            set => SetValue(MaxLinesProperty, value);
        }

        public VirtualizedConsoleTextBox()
        {
            IsReadOnly = true;
            Background = Brushes.Transparent;
            BorderThickness = new Thickness(0);
            Document = new FlowDocument
            {
                PagePadding = new Thickness(0),
                LineHeight = 1
            };
            FontFamily = new FontFamily("Consolas");
            FontSize = 12;
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            _currentParagraph = new Paragraph();
            Document.Blocks.Add(_currentParagraph);

            for (int i = 0; i < 50; i++)
            {
                _paragraphPool.Enqueue(new Paragraph());
            }
        }

        private static void OnMaxLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VirtualizedConsoleTextBox box)
            {
                box._maxVisibleParagraphs = (int)e.NewValue;
            }
        }

        public void AppendLine(string text)
        {
            lock (_lock)
            {
                if (_isUpdating) return;

                Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    try
                    {
                        DoAppendLine(text);
                    }
                    catch { }
                });
            }
        }

        public void AppendLinesBatch(IEnumerable<string> lines)
        {
            lock (_lock)
            {
                if (_isUpdating) return;

                Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    try
                    {
                        _isUpdating = true;
                        foreach (var line in lines)
                        {
                            DoAppendLine(line);
                        }
                    }
                    finally
                    {
                        _isUpdating = false;
                    }
                });
            }
        }

        private void DoAppendLine(string text)
        {
            if (_currentParagraph == null)
            {
                _currentParagraph = new Paragraph();
                Document.Blocks.Add(_currentParagraph);
            }

            _currentParagraph.Inlines.Add(new Run(text));
            _currentParagraph.Inlines.Add(new LineBreak());

            var blockCount = Document.Blocks.Count;
            if (blockCount > _maxVisibleParagraphs)
            {
                var toRemove = blockCount - _maxVisibleParagraphs;
                for (int i = 0; i < toRemove; i++)
                {
                    var first = Document.Blocks.FirstBlock;
                    if (first != null)
                    {
                        Document.Blocks.Remove(first);
                    }
                }
            }

            CaretPosition = Document.ContentEnd;
            ScrollToEnd();
        }

        public void ClearContent()
        {
            lock (_lock)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    Document.Blocks.Clear();
                    _currentParagraph = new Paragraph();
                    Document.Blocks.Add(_currentParagraph);
                });
            }
        }
    }
}