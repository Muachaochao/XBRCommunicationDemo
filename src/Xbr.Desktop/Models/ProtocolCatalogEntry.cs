namespace Xbr.Desktop.Models;

public sealed record ProtocolCatalogEntry(
    string Category,
    string Vendor,
    string Product,
    string Transport,
    string Source,
    string Status);
