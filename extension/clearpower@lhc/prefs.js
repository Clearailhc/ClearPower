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

        const FLOW = ['always', 'on-ac', 'never'];
        const flow = new Adw.ComboRow({
            title: 'Flow animation',
            subtitle: 'Gentle sheen on the power-flow diagram while the popover is open',
            model: Gtk.StringList.new(['Always', 'Only on AC power', 'Never']),
        });
        flow.selected = Math.max(0, FLOW.indexOf(settings.get_string('flow-animation')));
        flow.connect('notify::selected', () => settings.set_string('flow-animation', FLOW[flow.selected]));
        group.add(flow);
        page.add(group);
        window.add(page);
    }
}
