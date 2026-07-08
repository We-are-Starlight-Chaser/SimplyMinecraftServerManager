// Copyright (c) 2026 We Are Starlight Chaser Team
// Licensed under the MIT License.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Documents;
using System.Windows.Media;

namespace SimplyMinecraftServerManager.Helpers
{
    public static partial class McColorParser
    {
        [GeneratedRegex(@"[§&](?:x(?:[§&][0-9a-fA-F]){6}|x[0-9a-fA-F]{6}|[0-9a-fk-orA-FK-OR])")]
        private static partial Regex ColorCodeRegex();
        private static readonly Lazy<Dictionary<char, SolidColorBrush>> _standardColors = new(() =>
        {
            var dict = new Dictionary<char, SolidColorBrush>
            {
                ['0'] = Freeze(Color.FromRgb(0x00, 0x00, 0x00)),
                ['1'] = Freeze(Color.FromRgb(0x00, 0x00, 0xAA)),
                ['2'] = Freeze(Color.FromRgb(0x00, 0xAA, 0x00)),
                ['3'] = Freeze(Color.FromRgb(0x00, 0xAA, 0xAA)),
                ['4'] = Freeze(Color.FromRgb(0xAA, 0x00, 0x00)),
                ['5'] = Freeze(Color.FromRgb(0xAA, 0x00, 0xAA)),
                ['6'] = Freeze(Color.FromRgb(0xFF, 0xAA, 0x00)),
                ['7'] = Freeze(Color.FromRgb(0xAA, 0xAA, 0xAA)),
                ['8'] = Freeze(Color.FromRgb(0x55, 0x55, 0x55)),
                ['9'] = Freeze(Color.FromRgb(0x55, 0x55, 0xFF)),
                ['a'] = Freeze(Color.FromRgb(0x55, 0xFF, 0x55)),
                ['b'] = Freeze(Color.FromRgb(0x55, 0xFF, 0xFF)),
                ['c'] = Freeze(Color.FromRgb(0xFF, 0x55, 0x55)),
                ['d'] = Freeze(Color.FromRgb(0xFF, 0x55, 0xFF)),
                ['e'] = Freeze(Color.FromRgb(0xFF, 0xFF, 0x55)),
                ['f'] = Freeze(Colors.White),
            };
            return dict;
        });

        private static readonly Lazy<Dictionary<string, SolidColorBrush>> _namedColors = new(() =>
        {
            var map = new Dictionary<string, SolidColorBrush>(StringComparer.OrdinalIgnoreCase)
            {
                ["black"] = _standardColors.Value['0'],
                ["dark_blue"] = _standardColors.Value['1'],
                ["dark_green"] = _standardColors.Value['2'],
                ["dark_aqua"] = _standardColors.Value['3'],
                ["dark_red"] = _standardColors.Value['4'],
                ["dark_purple"] = _standardColors.Value['5'],
                ["gold"] = _standardColors.Value['6'],
                ["gray"] = _standardColors.Value['7'],
                ["dark_gray"] = _standardColors.Value['8'],
                ["blue"] = _standardColors.Value['9'],
                ["green"] = _standardColors.Value['a'],
                ["aqua"] = _standardColors.Value['b'],
                ["red"] = _standardColors.Value['c'],
                ["light_purple"] = _standardColors.Value['d'],
                ["yellow"] = _standardColors.Value['e'],
                ["white"] = _standardColors.Value['f'],
            };
            return map;
        });

        private static SolidColorBrush Freeze(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        /// <summary>
        /// 入口：自动检测 JSON / MiniMessage / Legacy § 码
        /// </summary>
        public static List<Run> Parse(string? input, Brush? defaultForeground = null)
        {
            var runs = new List<Run>();
            if (string.IsNullOrWhiteSpace(input)) return runs;

            defaultForeground ??= SystemColors.ControlTextBrush;
            string trimmed = input.Trim();
            if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
            {
                try
                {
                    using var doc = JsonDocument.Parse(trimmed);
                    ParseJsonElement(doc.RootElement, runs, defaultForeground, false, false, false, false);
                    if (runs.Count > 0) return runs;
                }
                catch { /* 回退 */ }
            }

            // MiniMessage gradient
            if (trimmed.Contains("<gradient:", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var mmRuns = ParseMiniMessageGradient(trimmed, defaultForeground);
                    if (mmRuns.Count > 0) return mmRuns;
                }
                catch { /* 回退 */ }
            }

            return ParseLegacyMotd(input, defaultForeground);
        }

        #region JSON ChatComponent Parser
        private static void ParseJsonElement(
            JsonElement element, List<Run> runs, Brush inheritedFg,
            bool inheritedBold, bool inheritedItalic, bool inheritedUnderline, bool inheritedStrike)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (var item in element.EnumerateArray())
                        ParseJsonElement(item, runs, inheritedFg, inheritedBold, inheritedItalic, inheritedUnderline, inheritedStrike);
                    return;

                case JsonValueKind.String:
                    // 裸字符串作为纯文本处理
                    string rawText = element.GetString()!;
                    if (!string.IsNullOrEmpty(rawText))
                        runs.Add(CreateRun(rawText, inheritedFg, inheritedBold, inheritedItalic, inheritedUnderline, inheritedStrike));
                    return;

                case JsonValueKind.Object:
                    break;

                default:
                    return;
            }


            var fg = inheritedFg;
            bool bold = inheritedBold, italic = inheritedItalic, underline = inheritedUnderline, strike = inheritedStrike;

            // 颜色
            if (element.TryGetProperty("color", out var colorProp) && colorProp.ValueKind == JsonValueKind.String)
            {
                string colorStr = colorProp.GetString()!;
                if (colorStr.StartsWith('#') && colorStr.Length == 7 && TryParseHex(colorStr[1..], out var hexColor))
                    fg = Freeze(hexColor);
                else if (_namedColors.Value.TryGetValue(colorStr, out var namedBrush))
                    fg = namedBrush;
            }

            // 样式标志
            if (element.TryGetProperty("bold", out var bProp) && bProp.ValueKind == JsonValueKind.True) bold = true;
            else if (bProp.ValueKind == JsonValueKind.False) bold = false;

            if (element.TryGetProperty("italic", out var iProp) && iProp.ValueKind == JsonValueKind.True) italic = true;
            else if (iProp.ValueKind == JsonValueKind.False) italic = false;

            if (element.TryGetProperty("underlined", out var uProp) && uProp.ValueKind == JsonValueKind.True) underline = true;
            else if (uProp.ValueKind == JsonValueKind.False) underline = false;

            if (element.TryGetProperty("strikethrough", out var sProp) && sProp.ValueKind == JsonValueKind.True) strike = true;
            else if (sProp.ValueKind == JsonValueKind.False) strike = false;

            if (element.TryGetProperty("text", out var textProp))
            {
                if (textProp.ValueKind == JsonValueKind.String)
                {
                    string text = textProp.GetString()!;
                    if (!string.IsNullOrEmpty(text))
                        runs.Add(CreateRun(text, fg, bold, italic, underline, strike));
                }
                else if (textProp.ValueKind == JsonValueKind.Object || textProp.ValueKind == JsonValueKind.Array)
                {
                    // text 本身也可能是嵌套组件（罕见）
                    ParseJsonElement(textProp, runs, fg, bold, italic, underline, strike);
                }
            }

            // 递归处理 extra（可能是数组或单个对象）
            if (element.TryGetProperty("extra", out var extraProp))
                ParseJsonElement(extraProp, runs, fg, bold, italic, underline, strike);
        }

        #endregion

        #region Legacy § Code Parser

        private static List<Run> ParseLegacyMotd(string input, Brush defaultForeground)
        {
            var runs = new List<Run>();
            var currentFg = defaultForeground;
            bool bold = false, italic = false, underline = false, strikethrough = false;
            int lastIndex = 0;

            foreach (Match match in ColorCodeRegex().Matches(input))
            {
                if (match.Index > lastIndex)
                    runs.Add(CreateRun(input[lastIndex..match.Index], currentFg, bold, italic, underline, strikethrough));

                lastIndex = match.Index + match.Length;
                string raw = match.Value;
                char codeChar = raw[^1];

                if (char.ToLowerInvariant(codeChar) == 'x' ||
                    (raw.Length > 2 && char.ToLowerInvariant(raw[1]) == 'x'))
                {
                    string hex = new([.. raw.Skip(2).Where(c => "0123456789abcdefABCDEF".Contains(c))]);
                    if (hex.Length == 6 && TryParseHex(hex, out var color))
                        currentFg = Freeze(color);
                    continue;
                }

                char c = char.ToLowerInvariant(codeChar);
                switch (c)
                {
                    case >= '0' and <= '9' or >= 'a' and <= 'f':
                        if (_standardColors.Value.TryGetValue(c, out var brush)) currentFg = brush;
                        bold = italic = underline = strikethrough = false;
                        break;
                    case 'l': bold = true; break;
                    case 'm': strikethrough = true; break;
                    case 'n': underline = true; break;
                    case 'o': italic = true; break;
                    case 'r':
                        currentFg = defaultForeground;
                        bold = italic = underline = strikethrough = false;
                        break;
                }
            }

            if (lastIndex < input.Length)
                runs.Add(CreateRun(input[lastIndex..], currentFg, bold, italic, underline, strikethrough));

            return runs;
        }

        #endregion

        #region MiniMessage Gradient Parser

        private static List<Run> ParseMiniMessageGradient(string input, Brush defaultForeground)
        {
            var runs = new List<Run>();
            var gradientRegex = GradientRegex();

            int lastIndex = 0;
            foreach (Match match in gradientRegex.Matches(input))
            {
                if (match.Index > lastIndex)
                    runs.Add(CreateRun(input[lastIndex..match.Index], defaultForeground, false, false, false, false));

                string startHex = match.Groups[1].Value[1..];
                string endHex = match.Groups[2].Value[1..];
                string text = match.Groups[3].Value;

                if (TryParseHex(startHex, out var startColor) && TryParseHex(endHex, out var endColor) && text.Length > 0)
                {
                    for (int i = 0; i < text.Length; i++)
                    {
                        double t = text.Length == 1 ? 0 : (double)i / (text.Length - 1);
                        byte r = (byte)(startColor.R + (endColor.R - startColor.R) * t);
                        byte g = (byte)(startColor.G + (endColor.G - startColor.G) * t);
                        byte b = (byte)(startColor.B + (endColor.B - startColor.B) * t);
                        runs.Add(CreateRun(text[i].ToString(), Freeze(Color.FromRgb(r, g, b)), false, false, false, false));
                    }
                }

                lastIndex = match.Index + match.Length;
            }

            if (lastIndex < input.Length)
                runs.Add(CreateRun(input[lastIndex..], defaultForeground, false, false, false, false));

            return runs;
        }

        #endregion

        private static Run CreateRun(string text, Brush fg, bool bold, bool italic, bool underline, bool strike)
        {
            var run = new Run(text)
            {
                Foreground = fg,
                FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
                FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
            };
            if (underline) run.TextDecorations = TextDecorations.Underline;
            if (strike) run.TextDecorations = TextDecorations.Strikethrough;
            return run;
        }

        private static bool TryParseHex(string hex, out Color color)
        {
            color = default;
            try
            {
                byte r = byte.Parse(hex.AsSpan(0, 2), NumberStyles.HexNumber);
                byte g = byte.Parse(hex.AsSpan(2, 2), NumberStyles.HexNumber);
                byte b = byte.Parse(hex.AsSpan(4, 2), NumberStyles.HexNumber);
                color = Color.FromRgb(r, g, b);
                return true;
            }
            catch { return false; }
        }

        [GeneratedRegex(@"<gradient:(#[0-9a-fA-F]{6}):(#[0-9a-fA-F]{6})>(.*?)</gradient>", RegexOptions.IgnoreCase | RegexOptions.Singleline, "en-US")]
        private static partial Regex GradientRegex();
    }
}