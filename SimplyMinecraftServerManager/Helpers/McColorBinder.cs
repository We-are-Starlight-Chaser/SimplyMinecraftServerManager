// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Media;


namespace SimplyMinecraftServerManager.Helpers
{
    public static class McColorBinder
    {
        public static readonly DependencyProperty MotdProperty =
            DependencyProperty.RegisterAttached(
                "Motd", typeof(string), typeof(McColorBinder),
                new PropertyMetadata(null, OnMotdChanged));

        public static void SetMotd(DependencyObject obj, string value) => obj.SetValue(MotdProperty, value);
        public static string GetMotd(DependencyObject obj) => (string)obj.GetValue(MotdProperty);

        private static void OnMotdChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not TextBlock tb) return;
            if (!tb.IsLoaded)
            {
                RoutedEventHandler? handler = null;
                handler = (_, _) =>
                {
                    tb.Loaded -= handler;
                    UpdateInlines(tb, e.NewValue as string);
                };
                tb.Loaded += handler;
                return;
            }

            UpdateInlines(tb, e.NewValue as string);
        }

        private static void UpdateInlines(TextBlock tb, string? motd)
        {
            if (!tb.CheckAccess())
            {
                tb.Dispatcher.BeginInvoke(new Action(() => UpdateInlines(tb, motd)));
                return;
            }

            try
            {
                Debug.WriteLine($"[McColorBinder] MOTD raw: \"{motd}\"");
                Debug.WriteLine($"[McColorBinder] MOTD length: {motd?.Length}, contains §: {motd?.Contains('§')}, contains &: {motd?.Contains('&')}");
                var testRuns = McColorParser.Parse(motd);
                Debug.WriteLine($"[McColorBinder] Parsed runs count: {testRuns.Count}");
                for (int i = 0; i < Math.Min(testRuns.Count, 5); i++)
                    Debug.WriteLine($"  Run[{i}]: text=\"{testRuns[i].Text}\", fg={testRuns[i].Foreground}");
                tb.Inlines.Clear();

                if (!string.IsNullOrEmpty(motd))
                {
                    Brush defaultFg = tb.Foreground;

                    foreach (var run in McColorParser.Parse(motd, defaultFg))
                    {
                        tb.Inlines.Add(run);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[McColorBinder] Parse failed: {ex.Message}");
                tb.Text = motd ?? "";
            }
        }
    }
}