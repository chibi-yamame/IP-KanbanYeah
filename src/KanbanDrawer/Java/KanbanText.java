import java.awt.*;
import java.awt.image.BufferedImage;
import java.io.*;

/**
 * Text renderer for 看板屋's "公式と同じ" text mode. 看板屋 runs it with the Java that opens .jar files (the one InterPot3's
 * shortcut uses), one long-lived process, so the result is pixel-identical to the program's 文字 tool (TextTool.drawText: Font("SansSerif", PLAIN, 12) by default, drawString on the board's
 * BufferedImage, no anti-aliasing, lines split at '\n' and spaced by FontMetrics.getHeight()).
 *
 * Built by tools/textjar.sh into src/KanbanDrawer/Resources/kanban-text.jar (embedded in KanbanDrawer.exe).
 * Protocol (stdin / stdout, one request per line, until stdin closes, then the process exits):
 *   request : "<size> <bold 0|1> <text as base64 UTF-8>"
 *   answer  : "<width> <height> <base64 of width*height bytes, 1 = ink>"   (box: widest line x lines*height)
 */
public final class KanbanText {
    public static void main(String[] args) throws Exception {
        BufferedReader in = new BufferedReader(new InputStreamReader(System.in, "UTF-8"));
        PrintStream out = new PrintStream(new BufferedOutputStream(System.out), false, "US-ASCII");
        String line;
        while ((line = in.readLine()) != null) {
            line = line.trim();
            if (line.length() == 0) continue;
            try {
                String[] p = line.split(" ");
                int size = Integer.parseInt(p[0]);
                boolean bold = p[1].equals("1");
                String text = p.length > 2 ? new String(Base64.decode(p[2]), "UTF-8") : "";
                out.println(render(text, size, bold));
            } catch (Throwable e) {
                out.println("ERR " + e);
            }
            out.flush();
        }
        System.exit(0);
    }

    static String render(String text, int size, boolean bold) {
        Font font = new Font("SansSerif", bold ? Font.BOLD : Font.PLAIN, size);
        BufferedImage probe = new BufferedImage(1, 1, BufferedImage.TYPE_INT_RGB);
        Graphics2D pg = probe.createGraphics();
        pg.setFont(font);
        FontMetrics fm = pg.getFontMetrics();
        String[] lines = text.split("\n", -1);
        int h = fm.getHeight(), asc = fm.getMaxAscent(), w = 1;
        for (String s : lines) w = Math.max(w, fm.stringWidth(s));
        // a margin for glyphs reaching outside their advance (bold, italics of some fonts); cut off again below
        int m = size, W = w + 2 * m, H = lines.length * h + 2 * m;
        BufferedImage img = new BufferedImage(W, H, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = img.createGraphics();
        g.setColor(Color.white);
        g.fillRect(0, 0, W, H);
        g.setColor(Color.black);
        g.setFont(font);
        // as TextTool: line k drawn with its baseline at top + k*height + (height - maxAscent) ... + maxAscent
        for (int k = 0; k < lines.length; k++)
            if (lines[k].length() > 0) g.drawString(lines[k], m, m + k * h + h - fm.getMaxDescent() - fm.getLeading());
        int bw = w + 2, bh = lines.length * h; // box: one pixel either side for overhangs
        byte[] bits = new byte[bw * bh];
        for (int y = 0; y < bh; y++)
            for (int x = 0; x < bw; x++)
                if ((img.getRGB(m - 1 + x, m + y) & 0xFFFFFF) == 0) bits[y * bw + x] = 1;
        return bw + " " + bh + " " + Base64.encode(bits);
    }

    /** java.util.Base64 is Java 8+ only in name; kept local so the class needs nothing beyond Java 1.4 APIs. */
    static final class Base64 {
        static final char[] A = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/".toCharArray();

        static String encode(byte[] b) {
            StringBuilder sb = new StringBuilder((b.length + 2) / 3 * 4);
            for (int i = 0; i < b.length; i += 3) {
                int n = (b[i] & 255) << 16 | (i + 1 < b.length ? (b[i + 1] & 255) << 8 : 0) | (i + 2 < b.length ? b[i + 2] & 255 : 0);
                sb.append(A[n >> 18 & 63]).append(A[n >> 12 & 63]);
                sb.append(i + 1 < b.length ? A[n >> 6 & 63] : '=').append(i + 2 < b.length ? A[n & 63] : '=');
            }
            return sb.toString();
        }

        static byte[] decode(String s) {
            int[] v = new int[128];
            for (int i = 0; i < A.length; i++) v[A[i]] = i;
            int pad = s.endsWith("==") ? 2 : s.endsWith("=") ? 1 : 0;
            byte[] out = new byte[s.length() / 4 * 3 - pad];
            for (int i = 0, o = 0; i < s.length(); i += 4) {
                int n = v[s.charAt(i)] << 18 | v[s.charAt(i + 1)] << 12 | v[s.charAt(i + 2) & 127] << 6 | v[s.charAt(i + 3) & 127];
                for (int k = 0; k < 3 && o < out.length; k++) out[o++] = (byte) (n >> (16 - 8 * k));
            }
            return out;
        }
    }
}
