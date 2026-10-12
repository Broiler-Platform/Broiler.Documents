using Broiler.Graphics.Color;

namespace Broiler.Documents.Pagination;

internal static class LayoutColors
{
    /// <summary><paramref name="color"/> when it is set, otherwise <paramref name="fallback"/>.</summary>
    public static BColor Or(BColor color, BColor fallback) => color.IsEmpty ? fallback : color;
}
