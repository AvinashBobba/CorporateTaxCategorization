using System.Text.RegularExpressions;

namespace CorporateTaxCategorization.Api.Services;

public sealed class WorkbookStorage
{
    public string Root { get; }
    public string Inbox => Path.Combine(Root, "inbox");
    public WorkbookStorage(IConfiguration configuration, IWebHostEnvironment environment)
    {
        Root = Path.GetFullPath(configuration["WorkbookStorage:Root"] ?? Path.Combine(environment.ContentRootPath, "..", "data", "workbooks"));
        foreach (var area in new[] { "inbox", "archive", "rejected" }) Directory.CreateDirectory(Path.Combine(Root, area));
    }
    public static string ValidateClient(string clientId)
    {
        if (!Regex.IsMatch(clientId, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$"))
            throw new ArgumentException("Client ID must contain 1–64 letters, digits, underscores or hyphens.");
        return clientId.ToUpperInvariant();
    }
    public string ClientDirectory(string area, string clientId)
    {
        var path = Path.Combine(Root, area, ValidateClient(clientId));
        Directory.CreateDirectory(path);
        return path;
    }
}
