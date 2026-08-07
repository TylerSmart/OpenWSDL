namespace OpenWSDL;

internal static class FileNameHelper
{
    public static string SanitizeBaseName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        var s = new string(chars).Trim();
        return string.IsNullOrEmpty(s) ? "service" : s;
    }

    public static string DefaultPostmanPath(string defaultTitle) =>
        $"{SanitizeBaseName(defaultTitle)}.postman_collection.json";
}
