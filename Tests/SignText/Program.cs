using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using RouteFilter.Systems;
internal static class Program
{
    private static void Main(string[] args)
    {
        var output = args.Length == 0 ? Path.GetTempPath() : args[0]; Directory.CreateDirectory(output);
        foreach (var text in new[] { "货车","摩托车","公交车","Motorcycles","Garbage trucks","Public transport vehicles","道路养护作业车辆" })
        {
            using var bitmap = new Bitmap(SignTextLayout.Width,SignTextLayout.Height);
            using (var graphics = Graphics.FromImage(bitmap)) SignTextLayout.Draw(graphics,text);
            var minX=bitmap.Width; var maxX=0; var minY=bitmap.Height; var maxY=0; int dark=0;
            for (int x=0;x<bitmap.Width;x++) for (int y=0;y<bitmap.Height;y++)
            { var color=bitmap.GetPixel(x,y); if(color.R<100 && color.G<100 && color.B<100) { dark++;minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);minY=Math.Min(minY,y);maxY=Math.Max(maxY,y); } }
            if (dark < 100 || minX<18 || maxX>1182 || minY<7 || maxY>319) throw new Exception("Clipped or missing sign text: "+text);
            if (text.Length<=3 && maxY-minY<150) throw new Exception("Short category label too small: "+text);
            bitmap.Save(Path.Combine(output,text+".png"),ImageFormat.Png);
            Console.WriteLine("PASS measured label: "+text+" bounds="+minX+","+minY+"–"+maxX+","+maxY);
        }
    }
}
