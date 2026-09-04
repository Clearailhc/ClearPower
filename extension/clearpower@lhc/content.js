import Clutter from 'gi://Clutter';
import Gio from 'gi://Gio';
import Mtk from 'gi://Mtk';
import Shell from 'gi://Shell';

// Average linear luminance of the screen (0..1), from a ~50x30 re-render of the
// stage: ~1 ms paint + ~4 ms encode. Only a single mean leaves this function.
const SCALE = 0.04;

function linear(v) {
    v /= 255;
    return v <= 0.04045 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
}

export async function sampleAverageLuminance() {
    const stage = global.stage;
    const rect = new Mtk.Rectangle({x: 0, y: 0, width: stage.width, height: stage.height});
    const content = stage.paint_to_content(rect, SCALE, null, Clutter.PaintFlag.NO_CURSORS);
    const texture = content.get_texture();
    const stream = Gio.MemoryOutputStream.new_resizable();
    const pixbuf = await new Promise((resolve, reject) => {
        Shell.Screenshot.composite_to_stream(texture, 0, 0, -1, -1, 1, null, 0, 0, 1, stream, (o, res) => {
            try {
                resolve(Shell.Screenshot.composite_to_stream_finish(res));
            } catch (e) {
                reject(e);
            }
        });
    });
    stream.close(null);
    const px = pixbuf.get_pixels();
    const n = pixbuf.get_n_channels(), rs = pixbuf.get_rowstride();
    const w = pixbuf.get_width(), h = pixbuf.get_height();
    let sum = 0;
    for (let y = 0; y < h; y++) {
        for (let x = 0; x < w; x++) {
            const i = y * rs + x * n;
            sum += 0.2126 * linear(px[i]) + 0.7152 * linear(px[i + 1]) + 0.0722 * linear(px[i + 2]);
        }
    }
    return w * h > 0 ? sum / (w * h) : -1;
}
