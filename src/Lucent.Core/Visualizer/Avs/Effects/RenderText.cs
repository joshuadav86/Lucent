using System.Globalization;
using System.Text;

namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Render / Text (r_text.cpp): shows words from a list separated by semicolons, one at a time, changing every so
/// many frames or on beats, placed, outlined or shadowed, and blended as set.
/// </summary>
[AvsEffect(28)]
public sealed class RenderText : AvsEffect
{
    // Windows' DrawText alignment flags, as AVS saved them.
    private const int AlignCentre = 1;
    private const int AlignRight = 2;
    private const int AlignMiddle = 4;
    private const int AlignBottom = 8;

    // CHOOSEFONT and LOGFONT as 32-bit Windows laid them out.
    private const int ChooseFontSize = 60;
    private const int LogFontSize = 60;

    private const int Empty = -1;

    private readonly StringBuilder _word = new();
    private int _enabled = 1;
    private int _colour = 0xffffff;
    private int _blend;
    private int _blendAverage;
    private int _onBeat;
    private int _insertBlank;
    private int _randomPosition;
    private int _verticalAlign = AlignMiddle;
    private int _horizontalAlign = AlignCentre;
    private int _onBeatSpeed = 15;
    private int _normalSpeed = 15;
    private int _fontHeight;
    private int _fontWeight;
    private string? _text;
    private int _outline;
    private int _outlineColour;
    private int _xShift;
    private int _yShift;
    private int _outlineSize = 1;
    private int _randomWord;
    private int _shadow;

    // What's showing, as AVS kept track of it.
    private int _currentWord;
    private int _oddEven;
    private int _frames;
    private int _beatFrames;
    private int _shownHorizontal;
    private int _shownVertical;
    private int _shownXShift;
    private int _shownYShift;
    private bool _forceAlign = true;
    private bool _shiftInit = true;
    private bool _forceShift;
    private bool _forceRedraw = true;
    private int _left, _top, _right, _bottom;
    private int _lastWidth, _lastHeight;
    private float _lastScale;
    private int _oldHorizontal, _oldVertical, _oldWord = -1, _oldOutline = -1, _oldShadow = -1, _oldXShift, _oldYShift;
    private bool _oldBlend1, _oldBlend2, _oldBlend3;
    private string _oldText = string.Empty;
    private int[] _buffer = Array.Empty<int>();
    private int _drawnTop;
    private int _drawnBottom;

    public RenderText(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _onBeat = config.ReadInt(_onBeat);
        _insertBlank = config.ReadInt(_insertBlank);
        _randomPosition = config.ReadInt(_randomPosition);
        _verticalAlign = config.ReadInt(_verticalAlign);
        _horizontalAlign = config.ReadInt(_horizontalAlign);
        _onBeatSpeed = config.ReadInt(_onBeatSpeed);
        _normalSpeed = config.ReadInt(_normalSpeed);
        if (config.Remaining >= ChooseFontSize)
        {
            config.Position += ChooseFontSize;
        }

        if (config.Remaining >= LogFontSize)
        {
            var font = config.Slice(config.Position, LogFontSize);
            _fontHeight = font.ReadInt();
            font.Position = 16;
            _fontWeight = font.ReadInt();
            config.Position += LogFontSize;
        }

        var text = config.ReadString();
        _text = text.Length > 0 ? text : null;
        _outline = config.ReadInt(_outline);
        _outlineColour = config.ReadInt(_outlineColour);
        _xShift = config.ReadInt(_xShift);
        _yShift = config.ReadInt(_yShift);
        _outlineSize = config.ReadInt(_outlineSize);
        _randomWord = config.ReadInt(_randomWord);
        _shadow = config.ReadInt(_shadow);
        Restart();
        if (_text is not null)
        {
            Warn("Text is drawn in Lucent's own simple font rather than the Windows font the preset chose.");
        }
    }

    public override void Restart()
    {
        _forceAlign = true;
        _forceRedraw = true;
        _forceShift = true;
        _shiftInit = true;
        _currentWord = 0;
        _oddEven = 0;
        _frames = 0;
        _beatFrames = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var isBeat = context.IsBeat;
        if (_forceAlign)
        {
            _forceAlign = false;
            _shownHorizontal = _horizontalAlign;
            _shownVertical = _verticalAlign;
        }

        if (_shiftInit)
        {
            _shiftInit = false;
            _shownXShift = _xShift;
            _shownYShift = _yShift;
        }

        // Time for the next word: every so many frames, or on a beat once the last beat's word has had its time.
        if ((_onBeat == 0 && _frames >= _normalSpeed) || (_onBeat != 0 && isBeat && _beatFrames == 0))
        {
            if (!(_insertBlank != 0 && _oddEven % 2 == 0))
            {
                var words = WordCount() + 1;
                _currentWord = _randomWord != 0 ? Shared.Random.Next(words) : (_currentWord + 1) % words;
            }

            _oddEven = (_oddEven + 1) % 2;
        }

        if (_onBeat != 0 && isBeat && _beatFrames == 0)
        {
            _beatFrames = _onBeatSpeed;
        }

        var text = GetWord(_currentWord);
        if (_insertBlank != 0 && _oddEven == 0)
        {
            text = string.Empty;
        }

        var scale = Math.Max(1, (int)MathF.Round((_fontHeight == 0 ? 16 : Math.Abs(_fontHeight)) * context.DotScale / RenderFont.CellHeight));
        var bold = _fontWeight >= 600 ? Math.Max(1, scale / 3) : 0;
        if ((_onBeat == 0 && _frames >= _normalSpeed) || (_onBeat != 0 && isBeat && _beatFrames == _onBeatSpeed))
        {
            _frames = 0;
            if (_randomPosition != 0 && w != 0 && h != 0)
            {
                // Anywhere the word fits.
                var textWidth = RenderFont.Width(text, scale, bold);
                var textHeight = RenderFont.Height(scale);
                _shownHorizontal = 0;
                if (textWidth < w)
                {
                    _shownXShift = RandomPercent((w - textWidth) / (float)w);
                }

                _shownVertical = 0;
                if (textHeight < h)
                {
                    _shownYShift = RandomPercent((h - textHeight) / (float)h);
                }

                _forceShift = true;
            }
            else
            {
                _shownHorizontal = _horizontalAlign;
                _shownVertical = _verticalAlign;
                _shownXShift = _xShift;
                _shownYShift = _yShift;
            }
        }

        if (_lastWidth != w || _lastHeight != h || _forceShift || _lastScale != context.DotScale)
        {
            if (_buffer.Length != w * h)
            {
                _buffer = new int[w * h];
            }

            _forceShift = false;
            _lastWidth = w;
            _lastHeight = h;
            _lastScale = context.DotScale;
            _left = (int)(_shownXShift * (float)w / 100.0f);
            _right = w + _left;
            _top = (int)(_shownYShift * (float)h / 100.0f);
            _bottom = h + _top;
            _forceRedraw = true;
        }

        var showing = !(_onBeat != 0 && _beatFrames == 0);
        var blend1 = _blend != 0 && showing;
        var blend2 = _blendAverage != 0 && showing;
        if (_forceRedraw || _oldHorizontal != _shownHorizontal || _oldVertical != _shownVertical || _oldWord != _currentWord
            || (text.Length > 0 && text != _oldText) || _oldBlend1 != blend1 || _oldBlend2 != blend2 || _oldBlend3 != showing
            || _oldOutline != _outline || _oldShadow != _shadow || _oldXShift != _shownXShift || _oldYShift != _shownYShift)
        {
            _forceRedraw = false;
            _oldHorizontal = _shownHorizontal;
            _oldVertical = _shownVertical;
            _oldWord = _currentWord;
            _oldText = text;
            _oldBlend1 = blend1;
            _oldBlend2 = blend2;
            _oldBlend3 = showing;
            _oldOutline = _outline;
            _oldShadow = _shadow;
            _oldXShift = _shownXShift;
            _oldYShift = _shownYShift;
            DrawText(text, w, h, scale, bold, (int)(_outlineSize * context.DotScale));
        }

        if (!showing || _drawnBottom <= _drawnTop)
        {
            Advance();
            return 0;
        }

        var buffer = _buffer;
        var mode = blend1 ? 1 : blend2 ? 3 : 0;
        context.ForEachRow(_drawnTop, _drawnBottom, y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = buffer[at];
                if (c != Empty)
                {
                    framebuffer[at] = AvsBlend.Line(framebuffer[at], c, mode);
                }
            }
        });
        Advance();
        return 0;
    }

    private void Advance()
    {
        if (_onBeat == 0)
        {
            _frames++;
        }

        if (_onBeat != 0 && _beatFrames != 0)
        {
            _beatFrames--;
        }
    }

    /// <summary>rand() % (int)(fraction * 100), the random placement AVS gave (0 where that would divide by nothing).</summary>
    private int RandomPercent(float fraction)
    {
        var range = (int)(fraction * 100.0f);
        return range > 0 ? Shared.Random.Next(range) : 0;
    }

    /// <summary>Draws the word into the text layer, with its outline or shadow, as DrawText placed it in the shifted screen rectangle.</summary>
    private void DrawText(string text, int w, int h, int scale, int bold, int offset)
    {
        var buffer = _buffer;
        Array.Fill(buffer, Empty, 0, w * h);
        _drawnTop = 0;
        _drawnBottom = 0;
        if (text.Length == 0)
        {
            return;
        }

        var textWidth = RenderFont.Width(text, scale, bold);
        var textHeight = RenderFont.Height(scale);
        var x = (_shownHorizontal & AlignRight) != 0 ? _right - textWidth
            : (_shownHorizontal & AlignCentre) != 0 ? _left + (_right - _left - textWidth) / 2
            : _left;
        var y = (_shownVertical & AlignBottom) != 0 ? _bottom - textHeight
            : (_shownVertical & AlignMiddle) != 0 ? _top + (_bottom - _top - textHeight) / 2
            : _top;
        var margin = 0;
        if (_outline != 0)
        {
            margin = Math.Abs(offset);
            for (var dy = -1; dy <= 1; dy++)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dy != 0)
                    {
                        RenderFont.Draw(buffer, w, h, text, x + dx * offset, y + dy * offset, scale, bold, _outlineColour & 0xffffff);
                    }
                }
            }
        }
        else if (_shadow != 0)
        {
            margin = Math.Abs(offset);
            RenderFont.Draw(buffer, w, h, text, x + offset, y + offset, scale, bold, _outlineColour & 0xffffff);
        }

        RenderFont.Draw(buffer, w, h, text, x, y, scale, bold, _colour & 0xffffff);
        _drawnTop = Math.Clamp(y - margin, 0, h);
        _drawnBottom = Math.Clamp(y + textHeight + margin, 0, h);
    }

    private int WordCount()
    {
        var count = 0;
        if (_text is not null)
        {
            foreach (var c in _text)
            {
                if (c == ';')
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>
    /// getWord: word <paramref name="n"/> of the list, with $(regNN), $(regNN:fmt), $(title), $(playpos) and
    /// $(playlen) filled in. Lucent can't give these effects the song's title or position, so those come out empty
    /// or as 0:00.
    /// </summary>
    private string GetWord(int n)
    {
        var text = _text;
        if (text is null)
        {
            return string.Empty;
        }

        var p = 0;
        for (var word = 0; word < n && p < text.Length; p++)
        {
            if (text[p] == ';')
            {
                word++;
            }
        }

        var result = _word.Clear();
        var maxLength = 255;
        while (p < text.Length && text[p] != ';' && maxLength > 0)
        {
            int end;
            string piece;
            if ((At(text, p, "$(playpos") || At(text, p, "$(playlen")) && (end = text.IndexOf(')', p + 9)) >= 0)
            {
                var digits = 0;
                if (p + 9 < text.Length && text[p + 9] == '.')
                {
                    digits = Math.Min(LeadingNumber(text, p + 10), 3);
                }

                piece = digits > 0 ? "0:00." + new string('0', digits) : "0:00";
                Warn("The song's position and length aren't available to presets in Lucent, so text shows them as 0:00.");
                p = end + 1;
            }
            else if (At(text, p, "$(title") && (end = text.IndexOf(')', p + 7)) >= 0)
            {
                piece = string.Empty;
                Warn("The song's title isn't available to presets in Lucent, so text leaves it out.");
                p = end + 1;
            }
            else if (At(text, p, "$(reg") && p + 6 < text.Length && char.IsAsciiDigit(text[p + 5]) && char.IsAsciiDigit(text[p + 6])
                && (end = text.IndexOf(')', p + 7)) >= 0)
            {
                var register = (text[p + 5] - '0') * 10 + (text[p + 6] - '0');
                p += 7;
                var format = new StringBuilder();
                if (p < text.Length && text[p] == ':')
                {
                    p++;
                    while (format.Length < 15 && p < text.Length && (char.IsAsciiDigit(text[p]) || text[p] == '.'))
                    {
                        format.Append(text[p++]);
                    }
                }

                piece = FormatNumber(Shared.Globals.Registers[register], format.ToString());
                p = end + 1;
            }
            else
            {
                result.Append(text[p++]);
                maxLength--;
                continue;
            }

            if (piece.Length > maxLength)
            {
                piece = piece[..maxLength];
            }

            result.Append(piece);
            maxLength -= piece.Length;
        }

        return result.ToString();
    }

    private static bool At(string text, int p, string token) =>
        string.Compare(text, p, token, 0, token.Length, StringComparison.OrdinalIgnoreCase) == 0 && p + token.Length <= text.Length;

    private static int LeadingNumber(string text, int p)
    {
        var value = 0;
        while (p < text.Length && char.IsAsciiDigit(text[p]) && value < 1000)
        {
            value = value * 10 + (text[p++] - '0');
        }

        return value;
    }

    /// <summary>printf's "%" + <paramref name="format"/> + "f": an optional width (0 first to pad with zeros) and precision.</summary>
    private static string FormatNumber(double value, string format)
    {
        var dot = format.IndexOf('.');
        var widthPart = dot >= 0 ? format[..dot] : format;
        var precisionPart = dot >= 0 ? format[(dot + 1)..] : null;
        var precision = precisionPart is null ? 6 : Math.Min(LeadingNumber(precisionPart, 0), 20);
        var width = Math.Min(LeadingNumber(widthPart, 0), 64);
        var number = value.ToString("F" + precision, CultureInfo.InvariantCulture);
        if (number.Length >= width)
        {
            return number;
        }

        if (widthPart.StartsWith('0'))
        {
            var negative = number.StartsWith('-');
            var digits = negative ? number[1..] : number;
            return (negative ? "-" : string.Empty) + digits.PadLeft(width - (negative ? 1 : 0), '0');
        }

        return number.PadLeft(width);
    }
}
