using System.Text;
using KanbanDrawer.Win;
using Xunit;
using Xunit.Abstractions;

namespace KanbanDrawer.Tests
{
    /// <summary>Diagnostics: which characters the GDI approximation draws differently from the program (prints only).</summary>
    public class OfficialTextDiag
    {
        readonly ITestOutputHelper output;
        public OfficialTextDiag(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void PerCharacter()
        {
            string chars = "看板あア、。「」ｱｲｳ ＡＢ１２★♪→〒InterPot0123456789!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~éü©°±×÷αβγДЖЯ‘’“”€";
            var oracle = KanbanTextOracle.Start();
            if (oracle == null) return;
            var sb = new StringBuilder();
            using (oracle)
                for (int size = OfficialText.MinSize; size <= OfficialText.MaxSize; size++)
                    foreach (char c in chars)
                    {
                        var j = oracle.Render(c.ToString(), size);
                        var t = OfficialText.Approximate(c.ToString(), size);
                        int diff = -1;
                        if (j.Width == t.Width && j.Height == t.Height)
                        {
                            diff = 0;
                            for (int i = 0; i < j.Bits.Length; i++) if (j.Bits[i] != t.Bits[i]) diff++;
                        }
                        if (diff != 0) sb.Append($"{size}:{c}(U+{(int)c:X4}) w {j.Width}/{t.Width} diff {diff}  ");
                    }
            output.WriteLine(sb.Length == 0 ? "all equal" : sb.ToString());
        }
    }
}
