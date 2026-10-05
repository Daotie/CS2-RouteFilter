using System;
using System.Drawing;
using System.Drawing.Drawing2D;
namespace RouteFilter.Systems;
// Rasterize installed fonts; distribution contains no font files.
internal static class SignTextLayout
{
    internal const int Width = 1200, Height = 326;
    internal static void Draw(Graphics graphics,string text)
    {
        graphics.Clear(Color.FromArgb(235,235,228));
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var family = new FontFamily("Microsoft YaHei");
        using var format = new StringFormat { Alignment = StringAlignment.Center,LineAlignment = StringAlignment.Center,FormatFlags = StringFormatFlags.NoWrap };
        var label = text;
        using var glyphs = new GraphicsPath();
        glyphs.AddString(label,family,(int)FontStyle.Bold,360,PointF.Empty,format);
        var bounds = glyphs.GetBounds();
        var scale = Math.Min(1152/Math.Max(1,bounds.Width),306/Math.Max(1,bounds.Height));
        var hasSpace = text.IndexOf(' ') >= 0;
        var cjk = text.Length > 0 && text[0] >= '\u4e00' && text[0] <= '\u9fff';
        if (bounds.Height*scale < Height*.65f && (hasSpace || cjk && text.Length > 5 || text.Length > 16))
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
            bounds = glyphs.GetBounds(); scale = Math.Min(1152/Math.Max(1,bounds.Width),306/Math.Max(1,bounds.Height));
        }
        using var transform = new Matrix(scale,0,0,scale,(Width-bounds.Width*scale)/2-bounds.X*scale,(Height-bounds.Height*scale)/2-bounds.Y*scale);
        glyphs.Transform(transform); graphics.FillPath(Brushes.Black,glyphs);
    }
}
