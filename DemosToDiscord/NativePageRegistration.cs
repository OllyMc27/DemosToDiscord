using SharedLibraryCore.Interfaces;

namespace DemosToDiscord;

internal static class NativePageRegistration
{
    public static void Add(IPageList pages, string name, string location, string icon)
    {
        pages.Pages[name] = location;

        // The bundle host can associate Phosphor icons with dynamic pages, but
        // the stable plugin SDK predates that property. Runtime discovery keeps
        // this build compatible without requiring another IW4MAdmin change.
        if (pages.GetType().GetProperty("PageIcons")?.GetValue(pages) is IDictionary<string, string> icons)
            icons[name] = icon;
    }
}
