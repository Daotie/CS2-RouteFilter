using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using RouteFilter.Systems;
internal static class Program
{
    private static void Main(string[] args)
    {
        try { Verify(args); }
        catch(Exception error) { Console.Error.WriteLine(error.GetType().Name+": "+error.Message); Environment.ExitCode=1; }
    }
    private static void Verify(string[] args)
    {
        var output = args.Length == 0 ? Path.GetTempPath() : args[0]; Directory.CreateDirectory(output);
        using(var originalMask=new Bitmap("RF-Plate_MaskMap.png"))
        using(var mask=new Bitmap("RF-Plate_MaskMap.png"))
        {
            PlateLegendAtlas.PrepareHdrpMask(mask);
            for(int y=0;y<mask.Height;y++)for(int x=0;x<mask.Width;x++)
            {
                var before=originalMask.GetPixel(x,y);var after=mask.GetPixel(x,y);
                if(after.G!=255||after.R!=before.R||after.B!=before.B||after.A!=before.A)
                    throw new Exception("HDRP mask must preserve metallic/detail/smoothness and allow ambient light");
            }
            if(mask.GetPixel(100,100).R!=0||mask.GetPixel(500,500).R!=255)
                throw new Exception("Front paint / rear metal distinction lost");
            Console.WriteLine("PASS HDRP mask: ambient light enabled, authored metallic/detail/smoothness preserved");
        }
        using(var transparent=new Bitmap(SignTextLayout.Width,SignTextLayout.Height))
        {
            using(var graphics=Graphics.FromImage(transparent)) SignTextLayout.Draw(graphics,"TRUCKS","en-US",true);
            if(transparent.GetPixel(0,0).A!=0 || transparent.GetPixel(50,50).A!=0) throw new Exception("Text overlay hides plate border");
            int ink=0;for(int x=0;x<transparent.Width;x++)for(int y=0;y<transparent.Height;y++)if(transparent.GetPixel(x,y).A>200)ink++;
            if(ink<100)throw new Exception("Transparent sign glyphs missing");
            transparent.Save(Path.Combine(output,"transparent-front-text.png"),ImageFormat.Png);
            Console.WriteLine("PASS transparent front glyphs: plate border/background remain visible");
        }
        using(var original=new Bitmap("RF-Plate_BaseColor.png"))
        using(var atlas=new Bitmap(original))
        using(var glyphs=new Bitmap(SignTextLayout.Width,SignTextLayout.Height))
        {
            using(var graphics=Graphics.FromImage(glyphs)) SignTextLayout.Draw(graphics,"载货汽车","zh-CN",true);
            PlateLegendAtlas.Composite(atlas,glyphs);
            int changed=0,face=0;
            for(int x=0;x<atlas.Width;x++)for(int y=0;y<atlas.Height;y++)
            {
                var after=atlas.GetPixel(x,y);var before=original.GetPixel(x,y);
                if(after.A!=255)throw new Exception("Baked atlas must be opaque");
                if(after.ToArgb()!=before.ToArgb())
                {
                    changed++;
                    if(x<28||x>292||y<30||y>993)throw new Exception("Baked legend changed border/back/edge");
                }
                if(x>40&&x<278&&y>42&&y<982&&after.R>200)face++;
            }
            if(changed<100||face<100000)throw new Exception("Missing baked text or black rectangle");
            atlas.Save(Path.Combine(output,"baked-front-atlas.png"),ImageFormat.Png);
            using(var front=atlas.Clone(new Rectangle(0,0,320,1024),PixelFormat.Format32bppArgb))
            {
                front.RotateFlip(RotateFlipType.Rotate90FlipNone);
                front.Save(Path.Combine(output,"baked-front-readable.png"),ImageFormat.Png);
            }
            Console.WriteLine("PASS opaque baked atlas: glyph ink present, light face retained, border/back/edge unchanged");
        }
        foreach (var locale in new[] { "zh-CN","en-GB","en-US" })
        foreach (var text in new[] { "载货汽车","大型载货汽车","道路养护车辆","指定大型载货汽车","指定道路养护车辆","ROAD MAINTENANCE","SELECTED LARGE GOODS VEHICLES","SELECTED EMERGENCY VEHICLES","MOTORCYCLES","GOODS VEHICLES","TRUCKS" })
        {
            if((text[0]>127)!=(locale=="zh-CN")) continue;
            using var bitmap = new Bitmap(SignTextLayout.Width,SignTextLayout.Height);
            using (var graphics = Graphics.FromImage(bitmap)) SignTextLayout.Draw(graphics,text,locale);
            var minX=bitmap.Width; var maxX=0; var minY=bitmap.Height; var maxY=0; int dark=0;
            for (int x=0;x<bitmap.Width;x++) for (int y=0;y<bitmap.Height;y++)
            { var color=bitmap.GetPixel(x,y); if(color.R<100 && color.G<100 && color.B<100) { dark++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y); } }
            if (dark < 100 || minX<54 || maxX>1146 || minY<30 || maxY>296) throw new Exception("Clipped or missing sign text: "+text);
            if (text[0]>127 && text.Length<=6 && maxY-minY<150) throw new Exception("Short category label too small: "+text);
            bitmap.Save(Path.Combine(output,locale+"-"+text+".png"),ImageFormat.Png);
            Console.WriteLine("PASS measured label: "+locale+" "+text+" bounds="+minX+","+minY+"–"+maxX+","+maxY);
        }
    }
}
