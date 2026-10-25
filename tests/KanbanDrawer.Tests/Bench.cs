using System.Diagnostics;
using System.Linq;
using KanbanDrawer.Core;
using KanbanDrawer.Core.Board;
using KanbanDrawer.Win;
using Xunit;
using Xunit.Abstractions;

namespace KanbanDrawer.Tests
{
    public class Bench
    {
        readonly ITestOutputHelper output;
        public Bench(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void FrameCost()
        {
            var doc = new BoardDoc();
            doc.Picture = new Raster(320, 240, Enumerable.Range(0, 76800).Select(i => unchecked((int)0xFF000000) | (int)((uint)i * 2654435761u % 0xFFFFFF)).ToArray());
            var stamp = new StampObject("s", new Raster(300, 200, Enumerable.Repeat(unchecked((int)0xFFFF8000), 60000).ToArray())) { Cx = 160, Cy = 120, Scale = 0.4 };
            doc.Objects.Add(stamp);
            var text = new GdiTextRasterizer();
            var t = new TextObject { Text = "テスト", Font = GdiTextRasterizer.DefaultFont(), Size = 30, Cx = 100, Cy = 60 };
            t.Measure(text);
            doc.Objects.Add(t);
            var opt = new QuantizeOptions();
            Quantizer.Quantize(Compositor.Build(doc, text), opt);

            var sw = Stopwatch.StartNew();
            Composite c = null;
            for (int i = 0; i < 10; i++) c = Compositor.Build(doc, text);
            output.WriteLine($"compose: {sw.ElapsedMilliseconds / 10.0} ms");
            sw.Restart();
            for (int i = 0; i < 10; i++) Quantizer.Quantize(c, opt);
            output.WriteLine($"quantize: {sw.ElapsedMilliseconds / 10.0} ms");
            sw.Restart();
            for (int i = 0; i < 10; i++) using (var b = ImageLoader.ToBitmap(c.ToRaster())) { }
            output.WriteLine($"to bitmap: {sw.ElapsedMilliseconds / 10.0} ms");
            sw.Restart();
            for (int i = 0; i < 10; i++) text.Rasterize(t, new byte[76800], 320, 240);
            output.WriteLine($"text only: {sw.ElapsedMilliseconds / 10.0} ms");
            sw.Restart();
            for (int i = 0; i < 10; i++) stamp.Render(new Composite(320, 240), text);
            output.WriteLine($"stamp only: {sw.ElapsedMilliseconds / 10.0} ms");
        }
    }
}
