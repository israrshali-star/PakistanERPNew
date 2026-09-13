using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PakistanAccountingERP.Application.Common;

/// <summary>
/// Converts Roman / English party names to Urdu script for ledger sharing.
/// Uses a business-term dictionary plus phonetic longest-match transliteration.
/// Already-Urdu text is left unchanged.
/// </summary>
public static class RomanUrduTransliterator
{
    private static readonly Dictionary<string, string> WordMap = RomanUrduExactMatchDictionary.Words;
    private static readonly HashSet<string> KeepLatin = RomanUrduExactMatchDictionary.KeepLatin;

    private static readonly Regex TokenRegex = new(
        @"c\s*/\s*o|[A-Za-z]+|[0-9]+|[^\sA-Za-z0-9]+|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex CamelCaseRegex = new(
        @"[A-Z]?[a-z]+|[A-Z]+(?![a-z])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly (string Phrase, string Urdu)[] Phrases =
    [
        ("transportation charges receive", "ٹرانسپورٹیشن چارجز وصول"),
        ("balance brought forward", "بیلنس برات فارورڈ"),
        ("cheque in clearing", "چیک کلیئرنگ میں"),
        ("cheque returned", "چیک واپس"),
        ("cheque payment", "چیک ادائیگی"),
        ("customer receipt", "کسٹمر رسید"),
        ("bank transfer", "بینک ٹرانسفر"),
        ("cash payment", "نقد ادائیگی"),
        ("opening balance", "اوپننگ بیلنس"),
        ("opening stock", "اوپننگ اسٹاک"),
        ("sales invoice", "سیلز انوائس"),
        ("debit note", "ڈیبٹ نوٹ"),
        ("credit note", "کریڈٹ نوٹ"),
        ("sales tax", "سیلز ٹیکس"),
        ("used tax", "یوزڈ ٹیکس"),
        ("low grade", "لو گریڈ"),
        ("jai namaz", "جائے نماز"),
        ("flat bright", "فلیٹ برائٹ"),
        ("baby lemon", "بےبی لیمون"),
        ("habib ur rehman", "حبیب الرحمن"),
        ("habib-ur-rehman", "حبیب الرحمن"),
        ("abdul rehman", "عبدالرحمان"),
        ("abdul rahman", "عبدالرحمان"),
        ("abdul hameed", "عبدالحمید"),
        ("abdul sattar", "عبدالستار"),
        ("abdul waheed", "عبدالوحید"),
        ("abdul mateen", "عبدالمتین"),
        ("abdul malik", "عبدالمالک"),
        ("wasi ud din", "وصی الدین"),
        ("wasi-ud-din", "وصی الدین"),
        ("self lifting", "سیلف لفٹنگ"),
        ("self lifing", "سیلف لفٹنگ"),
    ];

    // Longest-first phonetic tokens (Roman Urdu / English names).
    private static readonly (string Roman, string Urdu)[] Phonemes =
    [
        ("sch", "ش"),
        ("tch", "چ"),
        ("chh", "چھ"),
        ("khh", "کھ"),
        ("ghh", "گھ"),
        ("phh", "پھ"),
        ("thh", "تھ"),
        ("dhh", "دھ"),
        ("bhh", "بھ"),
        ("shh", "شھ"),
        ("ain", "عین"),
        ("gh", "غ"),
        ("kh", "خ"),
        ("ch", "چ"),
        ("sh", "ش"),
        ("zh", "ژ"),
        ("ph", "پھ"),
        ("th", "تھ"),
        ("dh", "دھ"),
        ("bh", "بھ"),
        ("jh", "جھ"),
        ("rh", "ڑھ"),
        ("ng", "نگ"),
        ("qu", "ق"),
        ("ee", "ی"),
        ("oo", "و"),
        ("aa", "آ"),
        ("ai", "ے"),
        ("ay", "ے"),
        ("au", "او"),
        ("ou", "او"),
        ("oi", "وئی"),
        ("ia", "یا"),
        ("ie", "ی"),
        ("ua", "وا"),
        ("ue", "وے"),
        ("a", "ا"),
        ("b", "ب"),
        ("c", "ک"),
        ("d", "د"),
        ("e", "ے"),
        ("f", "ف"),
        ("g", "گ"),
        ("h", "ہ"),
        ("i", "ی"),
        ("j", "ج"),
        ("k", "ک"),
        ("l", "ل"),
        ("m", "م"),
        ("n", "ن"),
        ("o", "و"),
        ("p", "پ"),
        ("q", "ق"),
        ("r", "ر"),
        ("s", "س"),
        ("t", "ت"),
        ("u", "و"),
        ("v", "و"),
        ("w", "و"),
        ("x", "کس"),
        ("y", "ی"),
        ("z", "ز"),
    ];

    /// <summary>
    /// Prefer an explicit Urdu name when sharing in Urdu; otherwise phonetic transliteration.
    /// </summary>
    public static string ResolveDisplayName(string englishName, string? urduName, bool useUrdu)
    {
        if (!useUrdu)
        {
            return englishName ?? string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(urduName))
        {
            return urduName.Trim();
        }

        return ToUrduScript(englishName);
    }

    /// <summary>
    /// Exact-match glossary (plus phonetic fallback) for a new party name.
    /// </summary>
    public static string? SuggestUrduName(string? englishName)
    {
        if (string.IsNullOrWhiteSpace(englishName))
        {
            return null;
        }

        var urdu = ToUrduScript(englishName);
        return string.IsNullOrWhiteSpace(urdu) ? null : urdu;
    }

    /// <summary>
    /// Keep a typed Urdu name; otherwise suggest from the English/Roman name.
    /// </summary>
    public static string? CoalescePartyNameUrdu(string? explicitUrdu, string? englishName)
    {
        if (!string.IsNullOrWhiteSpace(explicitUrdu))
        {
            return explicitUrdu.Trim();
        }

        return SuggestUrduName(englishName);
    }

    public static string ToUrduScript(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var trimmed = text.Trim();
        if (ContainsArabicScript(trimmed))
        {
            return trimmed;
        }

        // Normalize common abbreviations before tokenizing.
        trimmed = Regex.Replace(trimmed, @"\bc\s*/\s*o\b", "c/o", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        trimmed = ApplyPhrases(trimmed);

        var sb = new StringBuilder(trimmed.Length * 2);
        foreach (Match match in TokenRegex.Matches(trimmed))
        {
            var token = match.Value;
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (string.Equals(token, "c/o", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(WordMap["c/o"]);
                continue;
            }

            if (char.IsWhiteSpace(token[0]) || char.IsDigit(token[0]) || !char.IsLetter(token[0]))
            {
                sb.Append(token);
                continue;
            }

            sb.Append(MapToken(token));
        }

        return sb.ToString().Trim();
    }

    private static string ApplyPhrases(string text)
    {
        var result = text;
        foreach (var (phrase, urdu) in Phrases)
        {
            result = Regex.Replace(
                result,
                $@"\b{Regex.Escape(phrase)}\b",
                urdu,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return result;
    }

    private static string MapToken(string token)
    {
        if (WordMap.TryGetValue(token, out var mapped))
        {
            return mapped;
        }

        if (KeepLatin.Contains(token))
        {
            return token;
        }

        if (token.Contains('-', StringComparison.Ordinal))
        {
            var parts = token.Split('-');
            var joined = new StringBuilder();
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    joined.Append('-');
                }

                joined.Append(string.IsNullOrEmpty(parts[i]) ? parts[i] : MapToken(parts[i]));
            }

            return joined.ToString();
        }

        if (HasMixedCamelCase(token))
        {
            var parts = CamelCaseRegex.Matches(token);
            if (parts.Count > 1)
            {
                var joined = new StringBuilder();
                for (var i = 0; i < parts.Count; i++)
                {
                    if (i > 0)
                    {
                        joined.Append(' ');
                    }

                    joined.Append(MapToken(parts[i].Value));
                }

                return joined.ToString();
            }
        }

        return TransliterateWord(token);
    }

    private static bool HasMixedCamelCase(string token)
    {
        var hasLower = false;
        var hasUpper = false;
        foreach (var ch in token)
        {
            if (char.IsLower(ch))
            {
                hasLower = true;
            }
            else if (char.IsUpper(ch))
            {
                hasUpper = true;
            }

            if (hasLower && hasUpper)
            {
                return true;
            }
        }

        return false;
    }

    private static string TransliterateWord(string word)
    {
        if (string.IsNullOrEmpty(word))
        {
            return word;
        }

        if (WordMap.TryGetValue(word, out var mapped))
        {
            return mapped;
        }

        if (KeepLatin.Contains(word))
        {
            return word;
        }

        var lower = word.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        var i = 0;
        while (i < lower.Length)
        {
            var matched = false;
            foreach (var (roman, urdu) in Phonemes)
            {
                if (lower.Length - i < roman.Length)
                {
                    continue;
                }

                if (string.Compare(lower, i, roman, 0, roman.Length, StringComparison.Ordinal) == 0)
                {
                    // Leading "a" often becomes alef-madda for names (Aamir → آمیر-ish / عامر better via dict later)
                    if (i == 0 && roman == "a" && lower.Length > 1)
                    {
                        sb.Append('آ');
                    }
                    else
                    {
                        sb.Append(urdu);
                    }

                    i += roman.Length;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                sb.Append(lower[i]);
                i++;
            }
        }

        return sb.ToString();
    }

    private static bool ContainsArabicScript(string text)
    {
        foreach (var ch in text)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (category == UnicodeCategory.OtherLetter)
            {
                // Arabic block roughly U+0600–U+06FF and presentation forms
                if (ch is >= '\u0600' and <= '\u06FF'
                    or >= '\u0750' and <= '\u077F'
                    or >= '\uFB50' and <= '\uFDFF'
                    or >= '\uFE70' and <= '\uFEFF')
                {
                    return true;
                }
            }
        }

        return false;
    }
}
