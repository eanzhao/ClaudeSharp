using System.Globalization;

namespace Aexon.Core.Interactive;

/// <summary>
/// Approximate East Asian Width accounting. CJK ideographs, Hangul syllables,
/// fullwidth forms and emoji report two columns; everything else reports one.
/// Combining marks and format controls report zero so they don't shift the cursor.
///
/// Good enough for an interactive line editor — we are not shipping a grapheme
/// segmenter. If it ever matters, swap in ICU or an `East_Asian_Width.txt`
/// generated table.
/// </summary>
internal static class TerminalWidth
{
    public static int CharColumns(char ch)
    {
        if (char.IsHighSurrogate(ch) || char.IsLowSurrogate(ch))
            return 0; // surrogate halves are counted via surrogate pairs by callers that care

        if (ch == 0 || ch < 0x20)
            return 0;

        var category = CharUnicodeInfo.GetUnicodeCategory(ch);
        if (category is UnicodeCategory.NonSpacingMark
            or UnicodeCategory.EnclosingMark
            or UnicodeCategory.Format)
        {
            return 0;
        }

        return IsWide(ch) ? 2 : 1;
    }

    public static int StringColumns(string value)
    {
        if (string.IsNullOrEmpty(value))
            return 0;

        var total = 0;
        var i = 0;
        while (i < value.Length)
        {
            int codepoint;
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                codepoint = char.ConvertToUtf32(value[i], value[i + 1]);
                i += 2;
            }
            else
            {
                codepoint = value[i];
                i += 1;
            }

            if (codepoint == 0 || codepoint < 0x20)
                continue;

            var category = CharUnicodeInfo.GetUnicodeCategory(codepoint);
            if (category is UnicodeCategory.NonSpacingMark
                or UnicodeCategory.EnclosingMark
                or UnicodeCategory.Format)
            {
                continue;
            }

            total += IsWide(codepoint) ? 2 : 1;
        }

        return total;
    }

    private static bool IsWide(int cp) =>
        (cp >= 0x1100 && cp <= 0x115F) ||   // Hangul Jamo
        (cp >= 0x2E80 && cp <= 0x303E) ||   // CJK Radicals / Kangxi
        (cp >= 0x3041 && cp <= 0x33FF) ||   // Hiragana / Katakana / CJK Symbols / Bopomofo
        (cp >= 0x3400 && cp <= 0x4DBF) ||   // CJK Unified Ideographs Extension A
        (cp >= 0x4E00 && cp <= 0x9FFF) ||   // CJK Unified Ideographs
        (cp >= 0xA000 && cp <= 0xA4CF) ||   // Yi Syllables
        (cp >= 0xAC00 && cp <= 0xD7A3) ||   // Hangul Syllables
        (cp >= 0xF900 && cp <= 0xFAFF) ||   // CJK Compatibility Ideographs
        (cp >= 0xFE30 && cp <= 0xFE4F) ||   // CJK Compatibility Forms
        (cp >= 0xFF00 && cp <= 0xFF60) ||   // Fullwidth Forms
        (cp >= 0xFFE0 && cp <= 0xFFE6) ||   // Fullwidth Signs
        (cp >= 0x1F300 && cp <= 0x1FAFF) || // Emoji & pictographs
        (cp >= 0x20000 && cp <= 0x2FFFD) || // CJK Unified Ideographs Extension B+
        (cp >= 0x30000 && cp <= 0x3FFFD);   // CJK Unified Ideographs Extension G+
}
