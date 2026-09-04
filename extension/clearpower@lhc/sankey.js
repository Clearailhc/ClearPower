import Cairo from 'gi://cairo';
import GObject from 'gi://GObject';
import Pango from 'gi://Pango';
import PangoCairo from 'gi://PangoCairo';
import St from 'gi://St';

import {roundRect} from './batteryBar.js';

const C = {
    adapter: [0.29, 0.56, 0.89],
    battery: [0.36, 0.76, 0.48],
    pc: [0.55, 0.58, 0.62],
    soc: [0.56, 0.42, 0.91],
    disp: [0.88, 0.63, 0.29],
    other: [0.25, 0.72, 0.69],
};

export function fmtW(w, digits = null) {
    if (w == null || !isFinite(w))
        return '–';
    const d = digits ?? (w >= 10 ? 1 : 2);
    return `${w.toFixed(d)} W`;
}

export const Sankey = GObject.registerClass(
class Sankey extends St.DrawingArea {
    _init() {
        super._init({x_expand: true, height: 240});
        this._snap = null;
        this.connect('repaint', () => this._draw());
    }

    update(snap) {
        this._snap = snap;
        this.queue_repaint();
    }

    _model(s) {
        const onAc = !!s.on_ac;
        const batW = s.bat_w ?? 0;
        const sysW = Math.max(s.sys_w ?? 0, 0);
        const socOk = (s.soc_w ?? -1) >= 0;
        const socW = Math.max(s.soc_w ?? 0, 0);
        const dispW = Math.max(s.display_w ?? 0, 0);
        const otherW = Math.max(s.other_w ?? 0, 0);
        const cols = [[], [], []];
        const nodes = {};
        const flows = [];
        const add = (col, id, label, w, color) => {
            const n = {id, label, w, color, inTot: 0, outTot: 0, inOff: 0, outOff: 0};
            nodes[id] = n;
            cols[col].push(n);
            return n;
        };
        const flow = (a, b, w) => {
            if (w > 0.01)
                flows.push({a, b, w});
        };
        if (onAc) {
            const fromBat = Math.max(-batW, 0);
            const toBat = Math.max(batW, 0);
            const adToPc = Math.max(sysW - fromBat, 0);
            add(0, 'adapter', 'Adapter', adToPc + toBat, C.adapter);
            if (fromBat > 0)
                add(0, 'battery', 'Battery', fromBat, C.battery);
            if (toBat > 0)
                add(1, 'batchg', 'Battery', toBat, C.battery);
            add(1, 'pc', 'System', sysW, C.pc);
            flow('adapter', 'batchg', toBat);
            flow('adapter', 'pc', adToPc);
            flow('battery', 'pc', fromBat);
        } else {
            add(0, 'battery', 'Battery', sysW, C.battery);
            add(1, 'pc', 'System', sysW, C.pc);
            flow('battery', 'pc', sysW);
        }
        add(2, 'soc', 'SoC', socW, C.soc).unknown = !socOk;
        add(2, 'disp', 'Display', dispW, C.disp);
        add(2, 'other', 'Other', otherW, C.other);
        flow('pc', 'soc', socW);
        flow('pc', 'disp', dispW);
        flow('pc', 'other', otherW);
        for (const f of flows) {
            nodes[f.a].outTot += f.w;
            nodes[f.b].inTot += f.w;
        }
        return {cols, nodes, flows};
    }

    _draw() {
        const cr = this.get_context();
        const [W, H] = this.get_surface_size();
        const node = this.get_theme_node();
        const fg = node.get_foreground_color();
        if (!this._snap || W < 100) {
            cr.$dispose();
            return;
        }
        const m = this._model(this._snap);
        const pad = 6, gap = 10, minH = 44;
        const colW = [64, 64, 74];
        const colX = [pad, Math.round(W / 2 - colW[1] / 2), W - pad - colW[2]];

        // One global scale (px per watt) that fits every column, honouring minH.
        let scale = Infinity;
        for (const col of m.cols) {
            const tot = col.reduce((a, n) => a + n.w, 0);
            const avail = H - 2 * pad - gap * (col.length - 1);
            if (tot > 0)
                scale = Math.min(scale, avail / tot);
        }
        if (!isFinite(scale))
            scale = 1;
        for (let pass = 0; pass < 6; pass++) {
            let ok = true;
            for (const col of m.cols) {
                const avail = H - 2 * pad - gap * (col.length - 1);
                const need = col.reduce((a, n) => a + Math.max(minH, n.w * scale), 0);
                if (need > avail + 0.5) {
                    scale *= avail / need;
                    ok = false;
                }
            }
            if (ok)
                break;
        }
        m.cols.forEach((col, ci) => {
            const hs = col.map(n => Math.max(minH, n.w * scale));
            const total = hs.reduce((a, b) => a + b, 0) + gap * (col.length - 1);
            let y = (H - total) / 2;
            col.forEach((n, i) => {
                n.x = colX[ci]; n.y = y; n.w_px = colW[ci]; n.h = hs[i];
                n.inOff = (n.h - n.inTot * scale) / 2;
                n.outOff = (n.h - n.outTot * scale) / 2;
                y += hs[i] + gap;
            });
        });

        // Flows
        const labels = [];
        for (const f of m.flows) {
            const a = m.nodes[f.a], b = m.nodes[f.b];
            const t = Math.max(f.w * scale, 2);
            const x0 = a.x + a.w_px, y0 = a.y + a.outOff;
            const x1 = b.x, y1 = b.y + b.inOff;
            a.outOff += t; b.inOff += t;
            const mx = (x0 + x1) / 2;
            cr.moveTo(x0, y0);
            cr.curveTo(mx, y0, mx, y1, x1, y1);
            cr.lineTo(x1, y1 + t);
            cr.curveTo(mx, y1 + t, mx, y0 + t, x0, y0 + t);
            cr.closePath();
            const g = new Cairo.LinearGradient(x0, 0, x1, 0);
            g.addColorStopRGBA(0, ...a.color, 0.35);
            g.addColorStopRGBA(1, ...b.color, 0.35);
            cr.setSource(g);
            cr.fill();

        }

        // Nodes
        const font = node.get_font();
        const small = font.copy();
        small.set_size(Math.round(font.get_size() * 0.82));
        const bold = font.copy();
        bold.set_weight(Pango.Weight.BOLD);
        const layout = PangoCairo.create_layout(cr);
        for (const n of Object.values(m.nodes)) {
            roundRect(cr, n.x, n.y, n.w_px, n.h, 10);
            cr.setSourceRGBA(...n.color, 0.22);
            cr.fillPreserve();
            cr.setSourceRGBA(...n.color, 0.9);
            cr.setLineWidth(1.5);
            cr.stroke();
            layout.set_font_description(small);
            layout.set_text(n.label, -1);
            let [tw, th] = layout.get_pixel_size();
            layout.set_font_description(bold);
            const wText = n.unknown ? '–' : fmtW(n.w);
            layout.set_text(wText, -1);
            const [bw, bh] = layout.get_pixel_size();
            const twoLines = n.h >= th + bh + 6;
            cr.setSourceColor(fg);
            if (twoLines) {
                layout.set_font_description(small);
                layout.set_text(n.label, -1);
                cr.moveTo(n.x + (n.w_px - tw) / 2, n.y + (n.h - th - bh) / 2);
                PangoCairo.show_layout(cr, layout);
                layout.set_font_description(bold);
                layout.set_text(wText, -1);
                cr.moveTo(n.x + (n.w_px - bw) / 2, n.y + (n.h - th - bh) / 2 + th);
                PangoCairo.show_layout(cr, layout);
            } else {
                cr.moveTo(n.x + (n.w_px - bw) / 2, n.y + (n.h - bh) / 2);
                PangoCairo.show_layout(cr, layout);
            }
        }

        // Flow labels
        layout.set_font_description(small);
        for (const l of labels) {
            layout.set_text(l.text, -1);
            const [tw, th] = layout.get_pixel_size();
            cr.setSourceColor(fg);
            cr.moveTo(l.x - tw / 2, l.y - th / 2);
            PangoCairo.show_layout(cr, layout);
        }
        cr.$dispose();
    }
});
