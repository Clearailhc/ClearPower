import Adw from 'gi://Adw';
import Gtk from 'gi://Gtk';
import {ExtensionPreferences} from 'resource:///org/gnome/Shell/Extensions/js/extensions/prefs.js';

const KEYS = ['watts', 'percent', 'both', 'none'];

export default class ClearPowerPrefs extends ExtensionPreferences {
    fillPreferencesWindow(window) {
        const settings = this.getSettings();
        const page = new Adw.PreferencesPage();
        const group = new Adw.PreferencesGroup({title: 'Top bar'});
        const row = new Adw.ComboRow({
            title: 'Text next to the icon',
            model: Gtk.StringList.new(['System power (W)', 'Battery %', 'Both', 'None']),
        });
        row.selected = Math.max(0, KEYS.indexOf(settings.get_string('panel-text')));
        row.connect('notify::selected', () => settings.set_string('panel-text', KEYS[row.selected]));
        group.add(row);
        page.add(group);
        window.add(page);
    }
}
