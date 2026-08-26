// The string table. Every user-visible word goes through Lang.T, in both
// English and Ukrainian — tests/LangTests.cs checks the two tables agree on
// keys, on emptiness and on placeholder counts.
//
// The markings painted on the skin are deliberately *not* in here: "ON", "PRE",
// "SHUFFLE" and the band frequencies are part of the artwork, and a skin does
// not get retranslated any more than a faceplate does.
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Retrace
{
    static class Lang
    {
        public const string English = "en";
        public const string Ukrainian = "uk";

        static string current = English;

        public static string Current
        {
            get { return current; }
            set { current = value == Ukrainian ? Ukrainian : English; }
        }

        /// <summary>Picks the starting language from the system, so a Ukrainian
        /// Windows opens the player in Ukrainian without being asked.</summary>
        public static string SystemDefault()
        {
            try
            {
                string name = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
                return name == "uk" ? Ukrainian : English;
            }
            catch (ArgumentException) { return English; }
        }

        public static string T(string key)
        {
            Dictionary<string, string> table = current == Ukrainian ? Uk : En;
            string value;
            if (table.TryGetValue(key, out value)) return value;
            if (current != English && En.TryGetValue(key, out value)) return value;
            return key;
        }

        public static string T(string key, object arg0)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), arg0);
        }

        public static string T(string key, object arg0, object arg1)
        {
            return string.Format(CultureInfo.CurrentCulture, T(key), arg0, arg1);
        }

        internal static readonly Dictionary<string, string> En = new Dictionary<string, string>
        {
            { "nav.player", "Player" },
            { "nav.playlist", "Playlist" },
            { "nav.equaliser", "Equaliser" },
            { "nav.settings", "Settings" },

            { "card.now", "Now playing" },
            { "card.transport", "Transport" },
            { "card.output", "Output" },
            { "card.levels", "Levels" },
            { "card.playlist", "Playlist" },
            { "card.bands", "Bands" },
            { "card.curve", "Response" },
            { "card.scheme", "Colour scheme" },
            { "card.language", "Language" },
            { "card.about", "About" },

            { "now.nothing", "Nothing loaded" },
            { "now.hint", "Drop a file or a folder anywhere on this window" },
            { "now.position", "track {0} of {1}" },

            { "mode.shuffle", "Shuffle" },
            { "mode.repeat", "Repeat" },
            { "mode.repeat1", "Repeat one" },

            { "lvl.volume", "Volume" },
            { "lvl.balance", "Balance" },
            { "lvl.centre", "Centre" },

            { "stat.format", "Format" },
            { "stat.bitrate", "Bitrate" },
            { "stat.rate", "Sample rate" },
            { "stat.channels", "Channels" },
            { "stat.track", "Track" },
            { "stat.total", "In list" },
            { "stat.mono", "Mono" },
            { "stat.stereo", "Stereo" },

            { "status.idle", "Ready — no track loaded" },
            { "status.playing", "Playing" },
            { "status.paused", "Paused" },
            { "status.stopped", "Stopped" },

            { "vis.off", "display off" },

            { "eq.on", "On" },
            { "eq.off", "Off" },

            { "list.count", "{0} tracks · {1}" },
            { "list.none", "empty" },
            { "list.formats", "mp3 · wav · flac · m4a · aac · wma · ogg · opus" },


            { "display.nothing", "NO DISC" },
            { "display.stopped", "STOP" },

            { "list.add", "Files" },
            { "list.folder", "Folder" },
            { "list.load", "Load" },
            { "list.save", "Save" },
            { "list.remove", "Remove" },
            { "list.clear", "Clear" },
            { "list.empty", "Drop files or folders here" },
            { "list.reveal", "Show in Explorer" },

            { "menu.files", "Add files…" },
            { "menu.folder", "Add folder…" },
            { "menu.playlist", "Playlist editor" },
            { "menu.equaliser", "Equaliser" },
            
                        { "eq.reset", "Reset" },
            { "eq.preset", "Presets" },

            { "dialog.audio", "Audio files" },
            { "dialog.playlists", "Playlists" },
            { "dialog.all", "All files" },
            { "dialog.openFiles", "Add files to the playlist" },
            { "dialog.openFolder", "Add every track in a folder" },
            { "dialog.loadList", "Open a playlist" },
            { "dialog.saveList", "Save the playlist" },

            { "menu.language", "Language" },
            { "menu.english", "English" },
            { "menu.ukrainian", "Українська" },
            { "menu.about", "About" },
            { "menu.exit", "Exit" },

            { "about.line1", "A compact offline audio player." },
            { "about.line2", "One portable executable, no dependencies, no installer." },
            { "about.decoder", "Decoding: Media Foundation, built into Windows" },

            // ---- updates and installation ----
            { "card.updates", "Updates" },
            { "common.never", "never" },
            { "set.autoUpdate", "Update automatically from GitHub" },
            { "btn.checkUpdate", "Check for updates" },
            { "btn.installApp", "Install for this user" },
            { "btn.uninstallApp", "Remove from this user" },
            { "update.off", "Automatic updates are off — check by hand any time." },
            { "update.lastCheck", "Checked once a day. Last check: {0}" },
            { "update.checking", "Checking GitHub for a newer version…" },
            { "update.upToDate", "Version {0} is the latest one." },
            { "update.failed", "Could not check for updates. Check the connection and try again." },
            { "update.busy", "Stop playback first — installing an update restarts the player." },
            { "update.installing", "Updating to {0} — the player will restart in a few seconds…" },
            { "install.title", "Retrace — Setup" },
            { "install.installing", "Installing Retrace…" },
            { "install.failed", "Installation failed:\r\n" },
            { "uninstall.confirm", "Remove Retrace from this user account?" },
            { "uninstall.done", "Retrace has been removed." },
            { "uninstall.error", "Removal error: " }

        };

        internal static readonly Dictionary<string, string> Uk = new Dictionary<string, string>
        {
            { "nav.player", "Плеєр" },
            { "nav.playlist", "Список" },
            { "nav.equaliser", "Еквалайзер" },
            { "nav.settings", "Налаштування" },

            { "card.now", "Зараз грає" },
            { "card.transport", "Керування" },
            { "card.output", "Вихід" },
            { "card.levels", "Рівні" },
            { "card.playlist", "Список відтворення" },
            { "card.bands", "Смуги" },
            { "card.curve", "Крива" },
            { "card.scheme", "Кольорова гама" },
            { "card.language", "Мова" },
            { "card.about", "Про програму" },

            { "now.nothing", "Нічого не завантажено" },
            { "now.hint", "Перетягніть файл або теку будь-куди на це вікно" },
            { "now.position", "трек {0} з {1}" },

            { "mode.shuffle", "Вперемішку" },
            { "mode.repeat", "Повтор" },
            { "mode.repeat1", "Повтор треку" },

            { "lvl.volume", "Гучність" },
            { "lvl.balance", "Баланс" },
            { "lvl.centre", "Центр" },

            { "stat.format", "Формат" },
            { "stat.bitrate", "Бітрейт" },
            { "stat.rate", "Частота" },
            { "stat.channels", "Канали" },
            { "stat.track", "Трек" },
            { "stat.total", "У списку" },
            { "stat.mono", "Моно" },
            { "stat.stereo", "Стерео" },

            { "status.idle", "Готовий — трек не завантажено" },
            { "status.playing", "Відтворення" },
            { "status.paused", "Пауза" },
            { "status.stopped", "Зупинено" },

            { "vis.off", "індикатор вимкнено" },

            { "eq.on", "Увімк" },
            { "eq.off", "Вимк" },

            { "list.count", "{0} треків · {1}" },
            { "list.none", "порожньо" },
            { "list.formats", "mp3 · wav · flac · m4a · aac · wma · ogg · opus" },


            { "display.nothing", "НЕМАЄ ДИСКА" },
            { "display.stopped", "СТОП" },

            { "list.add", "Файли" },
            { "list.folder", "Тека" },
            { "list.load", "Відкрити" },
            { "list.save", "Зберегти" },
            { "list.remove", "Прибрати" },
            { "list.clear", "Очистити" },
            { "list.empty", "Перетягніть сюди файли або теку" },
            { "list.reveal", "Показати в Провіднику" },

            { "menu.files", "Додати файли…" },
            { "menu.folder", "Додати теку…" },
            { "menu.playlist", "Редактор списку" },
            { "menu.equaliser", "Еквалайзер" },
            
                        { "eq.reset", "Скинути" },
            { "eq.preset", "Пресети" },

            { "dialog.audio", "Звукові файли" },
            { "dialog.playlists", "Списки відтворення" },
            { "dialog.all", "Усі файли" },
            { "dialog.openFiles", "Додати файли до списку" },
            { "dialog.openFolder", "Додати всі треки з теки" },
            { "dialog.loadList", "Відкрити список відтворення" },
            { "dialog.saveList", "Зберегти список відтворення" },

            { "menu.language", "Мова" },
            { "menu.english", "English" },
            { "menu.ukrainian", "Українська" },
            { "menu.about", "Про програму" },
            { "menu.exit", "Вийти" },

            { "about.line1", "Компактний офлайновий аудіоплеєр." },
            { "about.line2", "Один портативний файл, без залежностей і без інсталятора." },
            { "about.decoder", "Декодування: Media Foundation, вбудований у Windows" },

            // ---- оновлення і встановлення ----
            { "card.updates", "Оновлення" },
            { "common.never", "ніколи" },
            { "set.autoUpdate", "Оновлювати автоматично з GitHub" },
            { "btn.checkUpdate", "Перевірити оновлення" },
            { "btn.installApp", "Встановити для цього користувача" },
            { "btn.uninstallApp", "Видалити для цього користувача" },
            { "update.off", "Автооновлення вимкнено — можна перевіряти вручну будь-коли." },
            { "update.lastCheck", "Перевірка раз на добу. Востаннє: {0}" },
            { "update.checking", "Перевіряю GitHub на новішу версію…" },
            { "update.upToDate", "Версія {0} — найновіша." },
            { "update.failed", "Не вдалося перевірити оновлення. Перевірте з'єднання і спробуйте ще раз." },
            { "update.busy", "Спершу зупиніть відтворення — встановлення оновлення перезапускає плеєр." },
            { "update.installing", "Оновлюю до {0} — плеєр перезапуститься за кілька секунд…" },
            { "install.title", "Retrace — встановлення" },
            { "install.installing", "Встановлюю Retrace…" },
            { "install.failed", "Не вдалося встановити:\r\n" },
            { "uninstall.confirm", "Видалити Retrace з цього облікового запису?" },
            { "uninstall.done", "Retrace видалено." },
            { "uninstall.error", "Помилка видалення: " }

        };
    }
}
