// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace SimplyMinecraftServerManager.Controls
{
    public class VirtualizedConsoleTextBox : RichTextBox
    {
        private readonly Stack<Paragraph> _paragraphPool = new(64);
        private readonly Stack<Run> _runPool = new(256);
        private readonly Stack<LineBreak> _lineBreakPool = new(256);
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
                _paragraphPool.Push(new Paragraph());
            for (int i = 0; i < 128; i++)
            {
                _runPool.Push(new Run());
                _lineBreakPool.Push(new LineBreak());
            }
        }

        private static void OnMaxLinesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is VirtualizedConsoleTextBox box)
                box._maxVisibleParagraphs = (int)e.NewValue;
        }

        public void AppendLine(string text)
        {
            lock (_lock)
            {
                if (_isUpdating) return;
                Application.Current?.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () =>
                {
                    try { DoAppendLine(text); }
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
                            DoAppendLine(line);
                    }
                    finally { _isUpdating = false; }
                });
            }
        }

        private Run GetRun() => _runPool.Count > 0 ? _runPool.Pop() : new Run();
        private LineBreak GetLineBreak() => _lineBreakPool.Count > 0 ? _lineBreakPool.Pop() : new LineBreak();
        private Paragraph GetParagraph() => _paragraphPool.Count > 0 ? _paragraphPool.Pop() : new Paragraph();

        private void DoAppendLine(string text)
        {
            if (_currentParagraph == null)
            {
                _currentParagraph = GetParagraph();
                Document.Blocks.Add(_currentParagraph);
            }

            var run = GetRun();
            run.Text = text;
            _currentParagraph.Inlines.Add(run);

            var lb = GetLineBreak();
            _currentParagraph.Inlines.Add(lb);

            var blockCount = Document.Blocks.Count;
            if (blockCount > _maxVisibleParagraphs)
            {
                int toRemove = blockCount - _maxVisibleParagraphs;
                for (int i = 0; i < toRemove; i++)
                {
                    var first = Document.Blocks.FirstBlock;
                    if (first is Paragraph p)
                    {
                        foreach (var inline in p.Inlines)
                        {
                            if (inline is Run r) { r.Text = ""; _runPool.Push(r); }
                            else if (inline is LineBreak br) _lineBreakPool.Push(br);
                        }
                        p.Inlines.Clear();
                        _paragraphPool.Push(p);
                    }
                    Document.Blocks.Remove(first);
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
                    foreach (var block in Document.Blocks)
                    {
                        if (block is Paragraph p)
                        {
                            foreach (var inline in p.Inlines)
                            {
                                if (inline is Run r) { r.Text = ""; _runPool.Push(r); }
                                else if (inline is LineBreak br) _lineBreakPool.Push(br);
                            }
                            p.Inlines.Clear();
                            _paragraphPool.Push(p);
                        }
                    }
                    Document.Blocks.Clear();
                    _currentParagraph = GetParagraph();
                    Document.Blocks.Add(_currentParagraph);
                });
            }
        }
    }
}
