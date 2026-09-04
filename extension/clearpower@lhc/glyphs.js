// Tiny vector glyphs for Sankey nodes, drawn with cairo so they follow the node colour
// and need no icon theme. All are designed on a 16 px box centred at (cx, cy).

function rr(cr, x, y, w, h, r) {
    r = Math.min(r, h / 2, w / 2);
    cr.newSubPath();
    cr.arc(x + w - r, y + r, r, -Math.PI / 2, 0);
    cr.arc(x + w - r, y + h - r, r, 0, Math.PI / 2);
    cr.arc(x + r, y + h - r, r, Math.PI / 2, Math.PI);
    cr.arc(x + r, y + r, r, Math.PI, 3 * Math.PI / 2);
    cr.closePath();
}

function line(cr, x0, y0, x1, y1) {
    cr.moveTo(x0, y0);
    cr.lineTo(x1, y1);
    cr.stroke();
}

export const GLYPHS = {
    adapter(cr, cx, cy) {              // wall plug: two prongs, body, cord
        line(cr, cx - 3, cy - 7, cx - 3, cy - 3);
        line(cr, cx + 3, cy - 7, cx + 3, cy - 3);
        rr(cr, cx - 6, cy - 3, 12, 7, 2.5);
        cr.stroke();
        line(cr, cx, cy + 4, cx, cy + 7.5);
    },
    battery(cr, cx, cy) {
        rr(cr, cx - 7.5, cy - 4, 13, 8, 1.5);
        cr.stroke();
        cr.rectangle(cx + 6, cy - 2, 2, 4);
        cr.fill();
        rr(cr, cx - 5.5, cy - 2, 6.5, 4, 0.8);
        cr.fill();
    },
    pc(cr, cx, cy) {                    // laptop
        rr(cr, cx - 6, cy - 5.5, 12, 8, 1.2);
        cr.stroke();
        line(cr, cx - 8, cy + 5, cx + 8, cy + 5);
    },
    cpu(cr, cx, cy) {                   // chip with pins
        rr(cr, cx - 5, cy - 5, 10, 10, 1.8);
        cr.stroke();
        cr.rectangle(cx - 2.2, cy - 2.2, 4.4, 4.4);
        cr.fill();
        for (const d of [-3, 0, 3]) {
            line(cr, cx + d, cy - 7.5, cx + d, cy - 5);
            line(cr, cx + d, cy + 5, cx + d, cy + 7.5);
            line(cr, cx - 7.5, cy + d, cx - 5, cy + d);
            line(cr, cx + 5, cy + d, cx + 7.5, cy + d);
        }
    },
    soc(cr, cx, cy) {                   // system-on-chip: package with four tiles
        rr(cr, cx - 6.5, cy - 6.5, 13, 13, 2.2);
        cr.stroke();
        for (const [dx, dy] of [[-4, -4], [1, -4], [-4, 1], [1, 1]])
            cr.rectangle(cx + dx, cy + dy, 3, 3);
        cr.fill();
    },
    gpu(cr, cx, cy) {                   // chip with a triangle (graphics)
        rr(cr, cx - 6.5, cy - 6.5, 13, 13, 2.2);
        cr.stroke();
        cr.moveTo(cx, cy - 3.6);
        cr.lineTo(cx + 3.8, cy + 3);
        cr.lineTo(cx - 3.8, cy + 3);
        cr.closePath();
        cr.fill();
        for (const d of [-3, 3]) {
            line(cr, cx + d, cy - 8, cx + d, cy - 6.5);
            line(cr, cx + d, cy + 6.5, cx + d, cy + 8);
            line(cr, cx - 8, cy + d, cx - 6.5, cy + d);
            line(cr, cx + 6.5, cy + d, cx + 8, cy + d);
        }
    },
    mem(cr, cx, cy) {                   // RAM module: four chips on a stick with a notch
        rr(cr, cx - 8, cy - 4, 16, 8, 1);
        cr.stroke();
        for (const d of [-6.5, -2.5, 1.5, 5.5])
            cr.rectangle(cx + d, cy - 2, 2.2, 3);
        cr.fill();
        line(cr, cx - 8, cy + 4, cx - 1, cy + 4);
        line(cr, cx + 1, cy + 4, cx + 8, cy + 4);
        line(cr, cx - 1, cy + 4, cx - 1, cy + 5.5);
        line(cr, cx + 1, cy + 4, cx + 1, cy + 5.5);
    },
    disp(cr, cx, cy) {                  // monitor
        rr(cr, cx - 7.5, cy - 6, 15, 10, 1.5);
        cr.stroke();
        line(cr, cx, cy + 4, cx, cy + 6.5);
        line(cr, cx - 4, cy + 6.5, cx + 4, cy + 6.5);
    },
    other(cr, cx, cy) {                 // ellipsis
        for (const d of [-5, 0, 5]) {
            cr.newSubPath();
            cr.arc(cx + d, cy, 1.6, 0, 2 * Math.PI);
            cr.fill();
        }
    },
};

/** Draw glyph `id` (falls back to `other`) with the current source colour. */
export function drawGlyph(cr, id, cx, cy) {
    cr.save();
    cr.setLineWidth(1.5);
    cr.setLineCap(1);  // ROUND
    (GLYPHS[id] ?? GLYPHS.other)(cr, cx, cy);
    cr.restore();
}
