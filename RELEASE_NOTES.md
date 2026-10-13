# 🧪 Cleaner 1.1.0-beta.5 — очистка и автоматизация / cleanup and automation

## 🇷🇺 Русский

Тестовая версия для Windows x64. Помогите проверить поведение приложения; beta ещё не готова к стабильному релизу.

- 🗺️ ПКМ на карте диска → расположение файла или папки. Компактные подписи, полное имя и путь в подсказке и под картой.
- 🧹 Выбор перед очисткой: карантин или безвозвратное удаление. Карантин выбран по умолчанию; удаление без карантина требует явного подтверждения.
- 🔎 Понятные пропуски: файл занят, нет доступа или уже исчез. В новых отчётах сохраняются причина Windows, версия и режим очистки.
- ⚡ Удаление карантина сохраняет журнал пакетами по 64 записи; после прерывания можно продолжить удаление.
- ⏱️ Автоочистка только пока Cleaner открыт: час, 6 часов, день или неделя. Только временные файлы пользователя, созданные и изменённые больше 24 часов назад; всегда в карантин, с исключениями и записью в историю.
- ⚙️ Запуск вместе с Windows по запросу в настройках. Автоматизация выключена по умолчанию.
- 📖 Обновлены руководства RU/EN и «Что нового?». История предыдущих версий сохранена.

**Важно:** безвозвратно удалённые файлы Cleaner не восстанавливает. Карантин на том же диске не освобождает место до удаления его содержимого. Точка восстановления не заменяет резервную копию личных файлов. Системные операции могут оставаться безвозвратными независимо от режима файлов. Перенос в карантин по-прежнему требует журналирования каждого файла; ускорено именно удаление карантина.

**Скачать:** в Assets выберите ZIP для win-x64 и распакуйте целиком. Запустите Cleaner.App.exe; Data должна оставаться рядом. .NET включён, установщик не нужен. Проверка SHA-256 необязательна. Beta не подписана; не отключайте защиту Windows ради запуска.

**Проверить:** ПКМ и длинные имена на карте, оба режима на ненужных тестовых файлах, отмену, заблокированные файлы, удаление большого тестового карантина. Автозапуск и автоочистку реального Temp проверяйте в отдельной VM; интервал не запускает очистку сразу. После переноса EXE повторно включите автозапуск из новой копии.

📣 [Ошибка или отзыв](https://github.com/Sking32/Cleaner/issues/new/choose) · [Руководство](https://github.com/Sking32/Cleaner/blob/master/README.ru.md)

Укажите версию, сборку Windows, шаги, ожидаемый и фактический результат. Перед отправкой отчёта или скриншота скройте личные пути и имена; не прикладывайте карантин и бэкапы.

## 🇬🇧 English

A Windows x64 test release. Help validate the app's behavior; this beta is not a stable release.

- 🗺️ Right-click a disk map cell to open its file or folder location. Compact labels, full names and paths in tooltips and below the map.
- 🧹 Choose quarantine or permanent deletion before cleanup. Quarantine remains the default; deletion without quarantine requires explicit confirmation.
- 🔎 Explained skips for busy, access-denied and missing files. New reports include Windows details, the app version and cleanup mode.
- ⚡ Quarantine deletion checkpoints every 64 entries and can resume after interruption.
- ⏱️ Automatic cleanup only while Cleaner is open: hourly, every 6 hours, daily or weekly. Only user temp files whose creation and modification are both older than 24 hours; always quarantined, with exclusions and history.
- ⚙️ Start with Windows on request in Settings. Automation is off by default.
- 📖 Updated RU/EN guides and What's new; earlier versions are retained.

**Important:** Cleaner cannot restore permanently deleted files. Quarantine on the same drive does not free space until its contents are deleted. A restore point does not replace personal file backups. System actions may remain permanent regardless of file mode. Moving files to quarantine still journals each file; this update speeds up quarantine deletion.

**Download:** choose the win-x64 ZIP under Assets, extract everything and run Cleaner.App.exe; keep Data beside it. .NET is included, no installer needed. SHA-256 verification is optional. The beta is unsigned; do not disable Windows protection to run it.

**Test:** map context menus and long names, both modes on disposable files, cancellation, busy files and large test quarantine deletion. Test startup and automatic cleanup of actual Temp in a disposable VM; enabling an interval does not clean immediately. After moving the EXE, re-enable startup from the new copy.

📣 [Bug or feedback](https://github.com/Sking32/Cleaner/issues/new/choose) · [Guide](https://github.com/Sking32/Cleaner/blob/master/README.md)

Include the app version, Windows build, steps, expected and actual result. Remove personal paths and names from reports and screenshots before posting; do not upload quarantine or backups.