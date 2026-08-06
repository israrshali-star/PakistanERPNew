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
    private static readonly Regex TokenRegex = new(
        @"c\s*/\s*o|[A-Za-z]+|[0-9]+|[^\sA-Za-z0-9]+|\s+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Dictionary<string, string> WordMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["and"] = "اینڈ",
        ["c/o"] = "کیئر آف",
        ["co"] = "کمپنی",
        ["company"] = "کمپنی",
        ["ltd"] = "لمیٹڈ",
        ["limited"] = "لمیٹڈ",
        ["pvt"] = "پرائیویٹ",
        ["private"] = "پرائیویٹ",
        ["factory"] = "فیکٹری",
        ["factroy"] = "فیکٹری",
        ["mill"] = "مل",
        ["mills"] = "ملز",
        ["silk"] = "سلک",
        ["textile"] = "ٹیکسٹائل",
        ["textiles"] = "ٹیکسٹائل",
        ["trading"] = "ٹریڈنگ",
        ["traders"] = "ٹریڈرز",
        ["trader"] = "ٹریڈر",
        ["merchants"] = "مرچنٹس",
        ["merchant"] = "مرچنٹ",
        ["carpet"] = "کارپٹ",
        ["carpets"] = "کارپٹ",
        ["hosiery"] = "ہوزیئری",
        ["hoisery"] = "ہوزیئری",
        ["elastic"] = "ایلاسٹک",
        ["dyeing"] = "ڈائینگ",
        ["dying"] = "ڈائینگ",
        ["communication"] = "کمیونیکیشن",
        ["communications"] = "کمیونیکیشن",
        ["industry"] = "انڈسٹری",
        ["industries"] = "انڈسٹریز",
        ["enterprises"] = "انٹرپرائزز",
        ["enterprise"] = "انٹرپرائز",
        ["stores"] = "اسٹورز",
        ["store"] = "اسٹور",
        ["center"] = "سینٹر",
        ["centre"] = "سینٹر",
        ["brothers"] = "برادرز",
        ["bros"] = "برادرز",
        ["son"] = "سن",
        ["sons"] = "سنز",
        ["mark"] = "مارک",
        ["add"] = "ایڈ",
        ["general"] = "جنرل",
        ["order"] = "آرڈر",
        ["supplier"] = "سپلائر",
        ["suppliers"] = "سپلائرز",
        ["yarn"] = "یارن",
        ["polyester"] = "پالیسٹر",
        ["cotton"] = "کاٹن",
        ["sports"] = "سپورٹس",
        ["star"] = "اسٹار",
        ["six"] = "سکس",
        ["normal"] = "نارمل",
        ["account"] = "اکاؤنٹ",
        ["al"] = "ال",
        ["the"] = "دی",
        ["of"] = "آف",
        ["for"] = "فار",
        ["with"] = "ودھ",
        // Common party-name tokens (company 3 samples)
        ["aamir"] = "عامر",
        ["habib"] = "حبیب",
        ["abbas"] = "عباس",
        ["abdul"] = "عبدال",
        ["hameed"] = "حمید",
        ["mateen"] = "متین",
        ["rehman"] = "رحمان",
        ["sattar"] = "ستار",
        ["abdullah"] = "عبداللہ",
        ["nawaz"] = "نواز",
        ["ahmad"] = "احمد",
        ["ahmed"] = "احمد",
        ["gilani"] = "گیلانی",
        ["majeed"] = "مجید",
        ["wahhab"] = "وہاب",
        ["wahab"] = "وہاب",
        ["baasit"] = "باسط",
        ["basit"] = "باسط",
        ["arian"] = "آریان",
        ["aziz"] = "عزیز",
        ["kashaf"] = "کشاف",
        ["mia"] = "ایم آئی اے",
        ["usman"] = "عثمان",
        ["rupali"] = "روپالی",
        ["ashraf"] = "اشرف",
        ["wali"] = "ولی",
        ["mushtaq"] = "مشتاق",
        ["saleem"] = "سلیم",
        ["younus"] = "یونس",
        ["waqar"] = "وقار",
        ["maqsood"] = "مقصود",
        ["arshad"] = "ارشد",
        ["hasnain"] = "حسنین",
        ["gulshan"] = "گلشن",
    };

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

        var sb = new StringBuilder(trimmed.Length * 2);
        foreach (Match match in TokenRegex.Matches(trimmed))
        {
            var token = match.Value;
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (string.Equals(token, "c/o", StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, "C/O", StringComparison.Ordinal))
            {
                sb.Append(WordMap["c/o"]);
                continue;
            }

            if (char.IsWhiteSpace(token[0]) || char.IsDigit(token[0]) || !char.IsLetter(token[0]))
            {
                // Keep slash groups that form c/o handled above; other punctuation as-is.
                sb.Append(token);
                continue;
            }

            if (WordMap.TryGetValue(token, out var mapped))
            {
                sb.Append(mapped);
                continue;
            }

            // Hyphenated compound like Al-Wahhab
            if (token.Contains('-', StringComparison.Ordinal))
            {
                var parts = token.Split('-');
                for (var i = 0; i < parts.Length; i++)
                {
                    if (i > 0)
                    {
                        sb.Append('-');
                    }

                    sb.Append(TransliterateWord(parts[i]));
                }

                continue;
            }

            sb.Append(TransliterateWord(token));
        }

        return sb.ToString().Trim();
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
