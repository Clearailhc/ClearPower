import Clutter from 'gi://Clutter';
import St from 'gi://St';
import * as Main from 'resource:///org/gnome/shell/ui/main.js';

import {t} from './i18n.js';

/** Full-screen white surface shown while the daemon sweeps brightness: maximal, known
 *  emission so the display's power-vs-brightness curve is measured with high SNR. */
export class CalibrationScreen {
    constructor(onCancel) {
        this._onCancel = onCancel;
        this._actor = null;
    }

    show() {
        if (this._actor)
            return;
        this._actor = new St.Widget({
            style: 'background-color: #ffffff;',
            reactive: true, x: 0, y: 0,
            width: global.stage.width, height: global.stage.height,
            layout_manager: new Clutter.BinLayout(),
        });
        this._label = new St.Label({
            style: 'color: #777777; font-size: 14pt; text-align: center;',
            x_align: Clutter.ActorAlign.CENTER, y_align: Clutter.ActorAlign.END,
            x_expand: true, y_expand: true,
        });
        this._label.clutter_text.line_alignment = 1;  // CENTER
        this._label.set_style('color: #777777; font-size: 14pt; padding-bottom: 48px;');
        this._actor.add_child(this._label);
        this._actor.connect('button-press-event', () => {
            this._onCancel();
            return Clutter.EVENT_STOP;
        });
        Main.layoutManager.addTopChrome(this._actor);
    }

    update(progress) {
        if (this._label)
            this._label.text = `${t('calibrating', {p: Math.round(progress * 100)})}\n${t('calibrateHint')}`;
    }

    hide() {
        if (!this._actor)
            return;
        Main.layoutManager.removeChrome(this._actor);
        this._actor.destroy();
        this._actor = null;
        this._label = null;
    }
}
