using QuestPDF.Drawing;

namespace PakistanAccountingERP.Application.Common;

/// <summary>Registers a Windows Urdu-capable font for QuestPDF documents.</summary>
public static class UrduPdfFont
{
    public const string FamilyName = "LedgerUrdu";
    private static readonly Lazy<string> RegisteredFamily = new(Register);

    public static string Family => RegisteredFamily.Value;

    private static string Register()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "UrdType.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "NIRMALA.TTF"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialuni.ttf"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "segoeui.ttf"),
        };

        foreach (var path in candidates)
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
}
