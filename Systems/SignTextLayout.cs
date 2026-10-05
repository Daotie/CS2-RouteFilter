using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace RouteFilter.Systems;
// Rasterize installed fonts; distribution contains no font files.
internal static class SignTextLayout
{
    internal const int Width = 1200, Height = 326;
    private static bool Cjk(string text) => text.Length>0 && text[0]>127;
    internal static string Tier(string text) => text.Length<=(Cjk(text)?4:12)?"SHORT":text.Length<=(Cjk(text)?8:22)?"MEDIUM":text.Length<=(Cjk(text)?16:32)?"LONG":"VERY_LONG";
    internal static string FontStyleKey(string locale) => locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?"CJK-Sans-Bold":locale=="en-GB"?"British-Condensed-Bold":"American-Sans-Bold";
    private static FontFamily Family(string locale)
    {
        var candidates=locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase)?new[]{"Microsoft YaHei","Microsoft JhengHei"}:locale.StartsWith("ja",StringComparison.OrdinalIgnoreCase)?new[]{"Yu Gothic","Meiryo"}:locale=="en-GB"?new[]{"Arial Narrow","Arial"}:new[]{"Arial","Arial Narrow"};
        foreach(var name in candidates) try { return new FontFamily(name); } catch(ArgumentException) { }
        if(locale.StartsWith("zh",StringComparison.OrdinalIgnoreCase) || locale.StartsWith("ja",StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("No suitable installed CJK sign font; supplementary plate skipped");
        return new FontFamily(FontFamily.GenericSansSerif.Name);
    }
    internal static void Draw(Graphics graphics,string text,string locale="zh-CN",bool transparent=false)
    {
        graphics.Clear(transparent ? Color.Transparent : Color.FromArgb(235,235,228));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var family = Family(locale);
        using var format = new StringFormat { Alignment = StringAlignment.Center,LineAlignment = StringAlignment.Center,FormatFlags = StringFormatFlags.NoWrap };
        var label = text;
        using var glyphs = new GraphicsPath();
        glyphs.AddString(label,family,(int)FontStyle.Bold,360,PointF.Empty,format);
        var bounds = glyphs.GetBounds();
        var scale = Math.Min(1080/Math.Max(1,bounds.Width),254/Math.Max(1,bounds.Height));
        var hasSpace = text.IndexOf(' ') >= 0;
        var cjk = Cjk(text);
        var tier=Tier(text);
        if (bounds.Height*scale < Height*.58f && (!cjk && hasSpace || cjk && (tier=="LONG" || tier=="VERY_LONG") || !cjk && text.Length > 16))
        {
            var split = text.Length/2;
            if (hasSpace)
            {
                var distance = text.Length;
                for (int i=1;i<text.Length-1;i++)
                    if (text[i]==' ' && Math.Abs(i-text.Length/2)<distance) { distance=Math.Abs(i-text.Length/2); split=i; }
            }
            label = text.Substring(0,split).Trim()+"\n"+text.Substring(split).Trim();
            glyphs.Reset(); glyphs.AddString(label,family,(int)FontStyle.Bold,360,PointF.Empty,format);
            bounds = glyphs.GetBounds(); scale = Math.Min(1080/Math.Max(1,bounds.Width),254/Math.Max(1,bounds.Height));
        }
        using var transform = new Matrix(scale,0,0,scale,(Width-bounds.Width*scale)/2-bounds.X*scale,(Height-bounds.Height*scale)/2-bounds.Y*scale);
        glyphs.Transform(transform); graphics.FillPath(Brushes.Black,glyphs);
    }
}
