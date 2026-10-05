using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace RouteFilter.Systems;

internal static class PlateLegendAtlas
{
    // The authored mask's green channel is unused; HDRP reads it as occlusion.
    // Preserve metallic/detail/smoothness while making ambient light unoccluded.
    internal static void PrepareHdrpMask(Bitmap mask)
    {
        var data=mask.LockBits(new Rectangle(0,0,mask.Width,mask.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try
        {
            var row=new byte[mask.Width*4];
            for(int y=0;y<mask.Height;y++)
            {
                var pointer=System.IntPtr.Add(data.Scan0,y*data.Stride);
                Marshal.Copy(pointer,row,0,row.Length);
                for(int x=0;x<mask.Width;x++)row[x*4+1]=255;
                Marshal.Copy(row,0,pointer,row.Length);
            }
        }
        finally { mask.UnlockBits(data); }
    }
    // Authored RF-Plate +Z UVs: u=(.125-y)*1.25, v=.5-x*1.25.
    // Image coordinates use 1-v. Composite only the existing label region,
    // preserving its face, border, rear and edge colors. No GPU alpha layer.
    internal static void Composite(Bitmap atlas,Bitmap glyphs)
    {
        using var graphics=Graphics.FromImage(atlas);
        graphics.CompositingMode=CompositingMode.SourceOver;
        graphics.InterpolationMode=InterpolationMode.HighQualityBicubic;
        graphics.DrawImage(glyphs,new[]{
            new PointF(.02875f*atlas.Width,.96875f*atlas.Height),
            new PointF(.02875f*atlas.Width,.03125f*atlas.Height),
            new PointF(.28375f*atlas.Width,.96875f*atlas.Height)
        });
    }
}
