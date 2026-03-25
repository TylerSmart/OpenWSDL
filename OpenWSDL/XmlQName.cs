namespace OpenWSDL;

internal readonly record struct XmlQName(string Namespace, string LocalName)
{
    public static XmlQName Parse(string? qualifiedName, Func<string, string> resolvePrefix)
    {
        if (string.IsNullOrWhiteSpace(qualifiedName))
            return new XmlQName("", "");

        var idx = qualifiedName.IndexOf(':');
        if (idx < 0)
            return new XmlQName("", qualifiedName);

        var prefix = qualifiedName[..idx];
        var local = qualifiedName[(idx + 1)..];
        return new XmlQName(resolvePrefix(prefix), local);
    }

    public bool IsEmpty => LocalName.Length == 0;
}
