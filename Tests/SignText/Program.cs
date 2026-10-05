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
