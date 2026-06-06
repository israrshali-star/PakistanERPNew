namespace PakistanAccountingERP.Web.Models;

public class NavMenuItem
{
    public string Title { get; set; } = string.Empty;
    public string? Icon { get; set; }
    public string? Url { get; set; }
    public string? Permission { get; set; }
    public string? Controller { get; set; }
    public string? Action { get; set; }
    public List<NavMenuItem> Children { get; set; } = new();
    public bool IsGroup => Children.Count > 0;
}
