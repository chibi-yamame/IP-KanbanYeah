import java.awt.AWTEvent;
import java.awt.Canvas;
import java.awt.Component;
import java.awt.Container;
import java.awt.EventQueue;
import java.awt.Frame;
import java.awt.Graphics2D;
import java.awt.Image;
import java.awt.KeyboardFocusManager;
import java.awt.MediaTracker;
import java.awt.RenderingHints;
import java.awt.Toolkit;
import java.awt.Window;
import java.awt.datatransfer.Clipboard;
import java.awt.datatransfer.DataFlavor;
import java.awt.datatransfer.Transferable;
import java.awt.event.AWTEventListener;
import java.awt.event.KeyEvent;
import java.awt.image.BufferedImage;
import java.io.EOFException;
import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.RandomAccessFile;
import java.lang.instrument.Instrumentation;
import java.lang.reflect.Field;
import java.util.List;
import javax.imageio.ImageIO;

/**
 * 看板屋のフルカラー直接書き込みのエージェント（docs/design/fullcolor.md）。InterPot3 のクライアントに
 * {@code -javaagent:kanban-agent.jar[=<pipe name>]} で入り、看板エディタ（interpot.client.board.EditCanvas）の
 * {@code pixels}（int[320*240]、0x00RRGGBB）を書き換える。ゲームの jar は変えず、サーバーへも何も送らない。
 *
 * <ul>
 * <li>名前付きパイプ {@code \\.\pipe\KanbanYeah-<SID>} のクライアント（看板屋がサーバー）。手順は設計書「パイプの手順」（版 1）:
 *   {@code HELLO 1 editor|none} を送り、{@code PUT 320 240\n} + RGB 230,400 バイトを受けて {@code OK} / {@code ERR <理由>} を返す。</li>
 * <li>看板エディタのウィンドウで Ctrl+Shift+V: クリップボードの画像を 320x240 に収めて書く（看板屋は不要）。</li>
 * </ul>
 * どんな例外もゲームに伝えない（標準エラーに出すだけ）。スレッドはすべてデーモン。
 */
public final class KanbanAgent {
    static final int W = 320, H = 240;
    static final int VERSION = 1;
    static final int IMAGE_BYTES = W * H * 3;
    /** 1 行の最大バイト数（\n を含む）。 */
    static final int MAX_LINE = 256;
    static final String EDITOR_CLASS = "interpot.client.board.EditCanvas";
    static final long RETRY_MS = 3000, POLL_MS = 50, EDITOR_CHECK_MS = 1000, READ_TIMEOUT_MS = 10000, WRITE_TIMEOUT_MS = 5000;

    private static boolean started;

    private KanbanAgent() { }

    public static void premain(String args, Instrumentation inst) {
        start(args);
    }

    /** 動的アタッチ用（看板屋は使わない。premain と同じ）。 */
    public static void agentmain(String args, Instrumentation inst) {
        start(args);
    }

    static synchronized void start(String args) {
        try {
            if (started) return;
            started = true;
            String name = args != null && args.trim().length() > 0 ? args.trim() : defaultPipeName();
            if (name != null) {
                Thread t = new Thread(new Link("\\\\.\\pipe\\" + name), "KanbanAgent");
                t.setDaemon(true);
                t.start();
            } else {
                log("user SID not available; pipe link disabled");
            }
        } catch (Throwable e) {
            log(e);
        }
        try {
            Paste.install();
        } catch (Throwable e) {
            log(e);
        }
    }

    /** {@code KanbanYeah-<SID>}。SID が得られなければ null。NTSystem は JDK 固有の API なのでリフレクションで呼ぶ。 */
    static String defaultPipeName() {
        try {
            Class<?> c = Class.forName("com.sun.security.auth.module.NTSystem");
            Object nt = c.newInstance();
            Object sid = c.getMethod("getUserSID").invoke(nt);
            if (sid instanceof String && ((String) sid).length() > 0) return "KanbanYeah-" + sid;
        } catch (Throwable e) {
            log(e);
        }
        return null;
    }

    static void log(String s) {
        try {
            System.err.println("[KanbanAgent] " + s);
        } catch (Throwable ignored) {
        }
    }

    static void log(Throwable e) {
        log(String.valueOf(e));
    }

    static void sleep(long ms) {
        try {
            Thread.sleep(ms);
        } catch (InterruptedException ignored) {
        }
    }

    /** 例外などを ASCII の 1 行に（制御文字は空白、ASCII 以外は '?'）。 */
    static String ascii(String s) {
        StringBuilder sb = new StringBuilder(s.length());
        for (int i = 0; i < s.length(); i++) {
            char c = s.charAt(i);
            sb.append(c < 0x20 || c == 0x7f ? ' ' : c > 0x7e ? '?' : c);
        }
        return sb.toString();
    }

    // ---- 看板エディタ ----

    /** 表示中の看板エディタ（クラス名の比較だけ。クラスはロードしない）。無ければ null。どのスレッドからでも（読むだけ）。 */
    static Component findEditor() {
        for (Frame f : Frame.getFrames()) {
            Component c = findIn(f);
            if (c != null) return c;
        }
        return null;
    }

    /** ウィンドウ w の部品木と、w が持つ（owned）ウィンドウから探す。 */
    static Component findIn(Window w) {
        Component c = find(w);
        if (c != null) return c;
        for (Window o : w.getOwnedWindows()) {
            c = findIn(o);
            if (c != null) return c;
        }
        return null;
    }

    static Component find(Component c) {
        if (c.getClass().getName().equals(EDITOR_CLASS) && c.isShowing()) return c;
        if (c instanceof Container) {
            for (Component k : ((Container) c).getComponents()) {
                Component r = find(k);
                if (r != null) return r;
            }
        }
        return null;
    }

    static Field field(Object o, String name) throws NoSuchFieldException {
        for (Class<?> c = o.getClass(); c != null; c = c.getSuperclass()) {
            try {
                Field f = c.getDeclaredField(name);
                f.setAccessible(true);
                return f;
            } catch (NoSuchFieldException ignored) {
            }
        }
        throw new NoSuchFieldException(name);
    }

    /**
     * 320x240 の ARGB を看板エディタに書く（設計書「書き込み」の手順 2）。アルファ 255 = その色、0 = 今の画素のまま、途中は合成。
     * イベントスレッドで実行し、最大 5 秒待つ。待ち切れなければ取り消して {@code ERR busy}（後から動いても書かない）。
     * 戻り値は {@code OK} / {@code ERR <理由>}。
     */
    static String write(final int[] argb) {
        final Object lock = new Object();
        final String[] result = { null };
        final boolean[] cancelled = { false };
        Runnable r = new Runnable() {
            public void run() {
                synchronized (lock) {
                    if (cancelled[0]) return;
                    String s;
                    try {
                        s = writeNow(argb);
                    } catch (Throwable e) {
                        s = "ERR " + e;
                    }
                    result[0] = s;
                    lock.notifyAll();
                }
            }
        };
        if (EventQueue.isDispatchThread()) {
            r.run();
            return result[0];
        }
        EventQueue.invokeLater(r);
        long deadline = System.currentTimeMillis() + WRITE_TIMEOUT_MS;
        synchronized (lock) {
            while (result[0] == null) {
                long left = deadline - System.currentTimeMillis();
                if (left <= 0) {
                    cancelled[0] = true;
                    return "ERR busy";
                }
                try {
                    lock.wait(left);
                } catch (InterruptedException ignored) {
                }
            }
            return result[0];
        }
    }

    /** イベントスレッド上で: エディタを探し直し、pixels → undoBuf に退避してから書き、repaint。 */
    static String writeNow(int[] argb) throws Exception {
        Component c = findEditor();
        if (c == null) return "ERR none";
        Object po = field(c, "pixels").get(c);
        Object uo = field(c, "undoBuf").get(c);
        if (!(po instanceof int[]) || !(uo instanceof int[])) return "ERR unexpected canvas size";
        int[] pixels = (int[]) po, undo = (int[]) uo;
        if (pixels.length != W * H || undo.length != W * H || c.getWidth() != W) return "ERR unexpected canvas size";
        System.arraycopy(pixels, 0, undo, 0, pixels.length); // ゲームの「元に戻す」（1 段）が使う退避
        for (int i = 0; i < pixels.length; i++) {
            int p = argb[i], a = p >>> 24;
            if (a == 255) {
                pixels[i] = p & 0xffffff;
            } else if (a != 0) {
                int o = undo[i];
                int rr = blend(p >> 16 & 255, o >> 16 & 255, a);
                int gg = blend(p >> 8 & 255, o >> 8 & 255, a);
                int bb = blend(p & 255, o & 255, a);
                pixels[i] = rr << 16 | gg << 8 | bb;
            }
        }
        c.repaint();
        return "OK";
    }

    static int blend(int s, int d, int a) {
        return (s * a + d * (255 - a) + 127) / 255;
    }

    // ---- 名前付きパイプ（クライアント） ----

    /** パイプ用スレッド: つなぐ → 1 接続を処理 → 切れたら 3 秒後につなぎ直す（無限）。 */
    static final class Link implements Runnable {
        final String path;

        Link(String path) {
            this.path = path;
        }

        public void run() {
            RandomAccessFile next = null;
            while (true) {
                try {
                    RandomAccessFile raf = next;
                    next = null;
                    if (raf == null) raf = tryOpen();
                    if (raf != null) {
                        Conn c;
                        try {
                            c = new Conn(this, raf);
                        } catch (Throwable e) {
                            try {
                                raf.close();
                            } catch (Throwable ignored) {
                            }
                            throw e;
                        }
                        try {
                            next = c.serve();
                        } catch (IOException e) {
                            log("pipe closed: " + ascii(String.valueOf(e)));
                        } finally {
                            c.close();
                        }
                    }
                } catch (Throwable e) {
                    log(e);
                }
                if (next == null) sleep(RETRY_MS);
            }
        }

        /** パイプを開く。無い（看板屋が起動していない）/ 使用中なら null。 */
        RandomAccessFile tryOpen() {
            try {
                return new RandomAccessFile(path, "rw");
            } catch (IOException e) {
                return null;
            }
        }
    }

    /**
     * 1 つの接続。1 本のスレッドで「available() で届いているか見る → 届いていれば読む」を繰り返し、読み待ちで固まらない
     * （同期ハンドルは読み待ちの間、書き込みも待たされる）。
     */
    static final class Conn {
        final Link link;
        final RandomAccessFile raf;
        /** available() 用。接続ごとに 1 つだけ作って持ち続け、raf と一緒に閉じる。 */
        final FileInputStream in;
        final byte[] buf = new byte[65536];
        int pos, lim;

        Conn(Link link, RandomAccessFile raf) throws IOException {
            this.link = link;
            this.raf = raf;
            this.in = new FileInputStream(raf.getFD());
        }

        void close() {
            try {
                in.close();
            } catch (Throwable ignored) {
            }
            try {
                raf.close();
            } catch (Throwable ignored) {
            }
        }

        /**
         * 接続を処理する。IOException で終われば切断（呼び出し側が閉じて 3 秒後に再接続）。null 以外を返したら、
         * 新しく開けたパイプ（= この接続はもう切れている）に乗り換える。
         */
        RandomAccessFile serve() throws IOException {
            boolean editor = editorShowing(false);
            send("HELLO " + VERSION + " " + (editor ? "editor" : "none"));
            long now = System.currentTimeMillis();
            long nextCheck = now + EDITOR_CHECK_MS, nextProbe = now + RETRY_MS;
            while (true) {
                if (fill()) {
                    handle(readLine());
                    continue;
                }
                now = System.currentTimeMillis();
                if (now >= nextProbe) {
                    // 切れたパイプでも available() は 0 を返すだけで切断が分からない。看板屋（最大インスタンス数 1）が
                    // 新しく待ち受けていれば開けるので、開けたらこの接続は切れていると分かる。つながっている間は「使用中」で開けない。
                    nextProbe = now + RETRY_MS;
                    RandomAccessFile n = link.tryOpen();
                    if (n != null) return n;
                }
                if (now >= nextCheck) {
                    nextCheck = now + EDITOR_CHECK_MS;
                    boolean e = editorShowing(editor);
                    if (e != editor && !fill()) { // 届いている要求があれば先にそれを処理する
                        editor = e;
                        send("HELLO " + VERSION + " " + (editor ? "editor" : "none"));
                    }
                }
                sleep(POLL_MS);
            }
        }

        boolean editorShowing(boolean previous) {
            try {
                return findEditor() != null;
            } catch (Throwable e) {
                return previous;
            }
        }

        void handle(String line) throws IOException {
            String[] p = line.split(" ", -1);
            if (p[0].equals("PUT")) {
                if (!isSize(p)) {
                    send("ERR bad size");
                    throw new IOException("bad size: " + line); // 画像の長さが分からず同期できないので閉じる
                }
                byte[] rgb = new byte[IMAGE_BYTES];
                readFully(rgb); // 途中で止まったら IOException（書かない）
                int[] argb = new int[W * H];
                for (int i = 0, j = 0; i < argb.length; i++, j += 3)
                    argb[i] = 0xff000000 | (rgb[j] & 255) << 16 | (rgb[j + 1] & 255) << 8 | (rgb[j + 2] & 255);
                send(write(argb));
            } else {
                send("ERR unknown command");
            }
        }

        static boolean isSize(String[] p) {
            if (p.length != 3) return false;
            try {
                return Integer.parseInt(p[1]) == W && Integer.parseInt(p[2]) == H;
            } catch (NumberFormatException e) {
                return false;
            }
        }

        void send(String s) throws IOException {
            String a = ascii(s);
            if (a.length() > MAX_LINE - 1) a = a.substring(0, MAX_LINE - 1);
            raf.write((a + "\n").getBytes("US-ASCII"));
        }

        /** 受け取り済みのバイトがあれば true（無ければ届いている分を読み込む。読み待ちはしない）。 */
        boolean fill() throws IOException {
            if (pos < lim) return true;
            int n = in.available();
            if (n <= 0) return false;
            int r = in.read(buf, 0, Math.min(n, buf.length));
            if (r < 0) throw new EOFException();
            pos = 0;
            lim = r;
            return r > 0;
        }

        /** 待つ（10 秒で見切る）。 */
        void await(long deadline) throws IOException {
            while (!fill()) {
                if (System.currentTimeMillis() > deadline) throw new IOException("read timeout");
                sleep(POLL_MS);
            }
        }

        /** 1 行（\n は含まない）。256 バイトを超える行・10 秒で終わらない行は IOException（接続を閉じる）。 */
        String readLine() throws IOException {
            long deadline = System.currentTimeMillis() + READ_TIMEOUT_MS;
            StringBuilder sb = new StringBuilder();
            while (true) {
                await(deadline);
                int b = buf[pos++] & 255;
                if (b == '\n') return sb.toString();
                if (sb.length() >= MAX_LINE - 1) throw new IOException("line too long");
                sb.append((char) b);
            }
        }

        void readFully(byte[] dst) throws IOException {
            long deadline = System.currentTimeMillis() + READ_TIMEOUT_MS;
            int off = 0;
            while (off < dst.length) {
                await(deadline);
                int n = Math.min(lim - pos, dst.length - off);
                System.arraycopy(buf, pos, dst, off, n);
                pos += n;
                off += n;
            }
        }
    }

    // ---- クリップボード（Ctrl+Shift+V） ----

    /** キーを観測するだけのリスナー（食べない・部品を差し替えない）。 */
    static final class Paste implements AWTEventListener {
        static volatile boolean running;

        static void install() {
            Toolkit.getDefaultToolkit().addAWTEventListener(new Paste(), AWTEvent.KEY_EVENT_MASK);
        }

        public void eventDispatched(AWTEvent ev) {
            try {
                if (ev.getID() != KeyEvent.KEY_PRESSED || !(ev instanceof KeyEvent)) return;
                KeyEvent k = (KeyEvent) ev;
                if (k.getKeyCode() != KeyEvent.VK_V || !k.isControlDown() || !k.isShiftDown()) return;
                Window w = KeyboardFocusManager.getCurrentKeyboardFocusManager().getActiveWindow();
                if (w == null || findIn(w) == null) return; // 看板エディタのウィンドウでだけ
                if (running) return;
                running = true;
                Thread t = new Thread(new Runnable() {
                    public void run() {
                        try {
                            String r = pasteClipboard();
                            if (r != null && !r.equals("OK")) log("paste: " + r);
                        } catch (Throwable e) {
                            log(e);
                        } finally {
                            running = false;
                        }
                    }
                }, "KanbanAgent-paste");
                t.setDaemon(true);
                t.start();
            } catch (Throwable e) {
                log(e);
            }
        }
    }

    /** クリップボードの画像を書く。画像が無ければ null（何もしない）。 */
    static String pasteClipboard() throws Exception {
        BufferedImage img = readClipboard();
        return img == null ? null : pasteImage(img);
    }

    /** 画像を 320x240 に収めて書く（余白・透明は今の画素のまま）。 */
    public static String pasteImage(BufferedImage img) {
        int[] argb = fit(img);
        return argb == null ? null : write(argb);
    }

    /** クリップボードの画像 → 無ければコピーしたファイルの先頭（ImageIO で読めるなら）。どちらも無ければ null。 */
    static BufferedImage readClipboard() throws Exception {
        Clipboard cb = Toolkit.getDefaultToolkit().getSystemClipboard();
        Transferable t = cb.getContents(null);
        if (t == null) return null;
        if (t.isDataFlavorSupported(DataFlavor.imageFlavor)) {
            Object o = t.getTransferData(DataFlavor.imageFlavor);
            if (o instanceof Image) {
                BufferedImage b = toBuffered((Image) o);
                if (b != null) return b;
            }
        }
        if (t.isDataFlavorSupported(DataFlavor.javaFileListFlavor)) {
            Object o = t.getTransferData(DataFlavor.javaFileListFlavor);
            if (o instanceof List && !((List<?>) o).isEmpty() && ((List<?>) o).get(0) instanceof File)
                return ImageIO.read((File) ((List<?>) o).get(0));
        }
        return null;
    }

    static BufferedImage toBuffered(Image img) throws InterruptedException {
        if (img instanceof BufferedImage) return (BufferedImage) img;
        MediaTracker mt = new MediaTracker(new Canvas());
        mt.addImage(img, 0);
        mt.waitForID(0, 10000);
        int w = img.getWidth(null), h = img.getHeight(null);
        if (w <= 0 || h <= 0) return null;
        BufferedImage b = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = b.createGraphics();
        g.drawImage(img, 0, 0, null);
        g.dispose();
        return b;
    }

    /**
     * 全体を 320x240 に収める（縦横比を保ち中央、拡大もする。双線形、大きく縮めるときは半分ずつ）。結果は ARGB（余白はアルファ 0）。
     */
    static int[] fit(BufferedImage src) {
        int sw = src.getWidth(), sh = src.getHeight();
        if (sw <= 0 || sh <= 0) return null;
        double s = Math.min((double) W / sw, (double) H / sh);
        int dw = Math.max(1, Math.min(W, (int) Math.round(sw * s)));
        int dh = Math.max(1, Math.min(H, (int) Math.round(sh * s)));
        BufferedImage cur = toArgb(src);
        while (cur.getWidth() / 2 >= dw && cur.getHeight() / 2 >= dh)
            cur = scaled(cur, cur.getWidth() / 2, cur.getHeight() / 2);
        BufferedImage out = new BufferedImage(W, H, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = out.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        g.drawImage(cur, (W - dw) / 2, (H - dh) / 2, dw, dh, null);
        g.dispose();
        return out.getRGB(0, 0, W, H, null, 0, W);
    }

    /** TYPE_INT_ARGB の複製。アルファがあるのに全画素が透明な画像（クリップボードの 32bit DIB で起こりうる）は不透明として扱う。 */
    static BufferedImage toArgb(BufferedImage src) {
        int w = src.getWidth(), h = src.getHeight();
        BufferedImage b = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = b.createGraphics();
        g.drawImage(src, 0, 0, null);
        g.dispose();
        if (src.getColorModel().hasAlpha()) {
            int[] px = src.getRGB(0, 0, w, h, null, 0, w);
            boolean any = false;
            for (int p : px) {
                if (p >>> 24 != 0) {
                    any = true;
                    break;
                }
            }
            if (!any) {
                for (int i = 0; i < px.length; i++) px[i] |= 0xff000000;
                b.setRGB(0, 0, w, h, px, 0, w);
            }
        }
        return b;
    }

    static BufferedImage scaled(BufferedImage src, int w, int h) {
        BufferedImage b = new BufferedImage(w, h, BufferedImage.TYPE_INT_ARGB);
        Graphics2D g = b.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BILINEAR);
        g.drawImage(src, 0, 0, w, h, null);
        g.dispose();
        return b;
    }
}
