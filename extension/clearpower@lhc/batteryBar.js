import Cairo from 'gi://cairo';
import GObject from 'gi://GObject';
import Pango from 'gi://Pango';
import PangoCairo from 'gi://PangoCairo';
import St from 'gi://St';

export function roundRect(cr, x, y, w, h, r) {
    r = Math.min(r, h / 2, w / 2);
    cr.newSubPath();
    cr.arc(x + w - r, y + r, r, -Math.PI / 2, 0);
    cr.arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
    cr.arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
    cr.arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
    cr.closePath();
}

const COLORS = {
    charging: [0.40, 0.78, 0.52],
    inhibited: [0.38, 0.70, 0.94],
    discharging: [0.62, 0.65, 0.69],
    forced: [0.95, 0.62, 0.32],
};

export const BatteryBar = GObject.registerClass(
class BatteryBar extends St.DrawingArea {
    _init() {
        super._init({x_expand: true, height: 28});
        this._state = {pct: 0, limit: 100, status: '', mode: 'limit', onAc: false};
        this.connect('repaint', () => this._draw());
    }

    update(partial) {
        Object.assign(this._state, partial);
        this.queue_repaint();
    }

    _draw() {
        const cr = this.get_context();
        const [w, h] = this.get_surface_size();
        const node = this.get_theme_node();
        const fg = node.get_foreground_color();
        const {pct, limit, status, mode, onAc} = this._state;
        const r = h / 2;

        roundRect(cr, 0, 0, w, h, r);
        cr.setSourceRGBA(0.5, 0.5, 0.5, 0.18);
        cr.fill();

        let kind = 'discharging';
        let glyph = '–';
        if (mode === 'discharge') {
            kind = 'forced'; glyph = '⤓';
        } else if (status === 'Charging') {
            kind = 'charging'; glyph = '⚡';
        } else if (onAc) {
            kind = 'inhibited'; glyph = '⏸';
        }
        const [cr_, cg, cb] = COLORS[kind];
        const fillW = Math.max(h, w * Math.min(pct, 100) / 100);
        const g = new Cairo.LinearGradient(0, 0, 0, h);
        g.addColorStopRGBA(0, cr_ * 1.08, cg * 1.08, cb * 1.08, 0.92);
        g.addColorStopRGBA(1, cr_ * 0.88, cg * 0.88, cb * 0.88, 0.92);
        roundRect(cr, 0, 0, fillW, h, r);
        cr.setSource(g);
        cr.fill();

        if (limit < 100) {
            const x = Math.round(w * limit / 100) + 0.5;
            cr.setDash([2, 3], 0);
            cr.setLineWidth(1.5);
            cr.moveTo(x, 5);
            cr.lineTo(x, h - 5);
            cr.setSourceRGBA(fg.red / 255, fg.green / 255, fg.blue / 255, 0.7);
            cr.stroke();
            cr.setDash([], 0);
        }

        const font = node.get_font().copy();
        font.set_weight(Pango.Weight.BOLD);
        const layout = PangoCairo.create_layout(cr);
        layout.set_font_description(font);
        const paint = (text, x, y, tw) => {
            layout.set_text(text, -1);
            if (x + tw <= fillW - 6) {
                cr.setSourceRGBA(0, 0, 0, 0.22);
                cr.moveTo(x + 1, y + 1);
                PangoCairo.show_layout(cr, layout);
                cr.setSourceRGBA(1, 1, 1, 0.95);
            } else {
                cr.setSourceColor(fg);
            }
            cr.moveTo(x, y);
            PangoCairo.show_layout(cr, layout);
        };
        layout.set_text(`${pct}%`, -1);
        const [tw, th] = layout.get_pixel_size();
        paint(`${pct}%`, 12, (h - th) / 2, tw);
        layout.set_text(glyph, -1);
        const [gw, gh] = layout.get_pixel_size();
        paint(glyph, (w - gw) / 2, (h - gh) / 2, gw);
        cr.$dispose();
    }
});
