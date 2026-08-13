using QuestPDF.Drawing;

namespace PakistanAccountingERP.Application.Common;

/// <summary>Registers Jameel Noori Nastaleeq (bundled, then Windows) for QuestPDF Urdu documents.</summary>
public static class UrduPdfFont
{
    public const string FamilyName = "LedgerUrdu";
    private static readonly Lazy<string> RegisteredFamily = new(Register);

    public static string Family => RegisteredFamily.Value;

    private static string Register()
    {
        foreach (var path in CandidateFontPaths())
        {
            if (!File.Exists(path))
            {
                continue;
            }

            try
            {
                using var stream = File.OpenRead(path);
                FontManager.RegisterFontWithCustomName(FamilyName, stream);
                return FamilyName;
            }
            catch
            {
                // Try next candidate.
            }
        }

        return "Arial";
    }

    private static IEnumerable<string> CandidateFontPaths()
    {
        var windowsFonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        var bundledName = "JameelNooriNastaleeq.ttf";

        foreach (var root in EnumerateSearchRoots())
        {
            yield return Path.Combine(root, "wwwroot", "fonts", bundledName);
            yield return Path.Combine(root, "fonts", bundledName);
            yield return Path.Combine(root, "Fonts", bundledName);
        }

        yield return Path.Combine(windowsFonts, "Jameel Noori Nastaleeq.ttf");
        yield return Path.Combine(windowsFonts, "UrdType.ttf");
        yield return Path.Combine(windowsFonts, "NIRMALA.TTF");
        yield return Path.Combine(windowsFonts, "arialuni.ttf");
        yield return Path.Combine(windowsFonts, "segoeui.ttf");
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (seen.Add(dir.FullName))
            {
                yield return dir.FullName;
            }
        }
    }
}
