# Cleaner 1.1.0-beta.7.1 — проверка тестировщиками / tester checklist

## 🇷🇺 На обычном компьютере: знакомство без очистки

1. Распакуйте готовый win-x64 ZIP целиком. `Data` остаётся рядом с `Cleaner.App.exe`; .NET включён. Не запускайте старую и новую копии одновременно.
2. Прочитайте памятку первого запуска. Для знакомства выберите «Понятно, продолжить». Создание точки восстановления — отдельное системное действие, не обязательное для просмотра интерфейса.
3. Проверьте RU/EN Windows, ручной выбор языка и «Как в Windows» после перезапуска. Ранее выбранные настройки должны сохраниться; другие языки дают английский.
4. В шапке и «Что нового?» должна быть `1.1.0-beta.7.1`, предыдущие выпуски сохранены. История и настройки находятся отдельно от основных инструментов.
5. В простом режиме найдите категории пользовательского и системного Temp и профиль «Для игроков». Раскройте карточку: понятны ли последствия, условия и восстановление? Обычный режим показывает дополнительные кэши, истории и диагностику без автоматического выбора. Экспертный добавляет системные действия с предупреждениями; не применяйте их при знакомстве с интерфейсом.
6. Нажмите «Проверить компьютер». Анализ не перемещает и не удаляет файлы. Проверьте объёмы, пропуски, график категорий и переход со столбца к деталям. Отмените ещё один анализ: интерфейс должен снова стать доступен.

Проверьте названия «Мягкая», «Стандартная», «Глубокая». «Все» ставит галочки доступных категорий текущего режима, «Ничего» снимает их; файлы при этом не изменяются. После массового выбора пресет не должен оставаться выделенным. Примените снова любой пресет и проверьте соответствующий выбор. Во время анализа кнопки «Все», «Ничего» и список пресетов недоступны. В экспертном режиме «Все» включает системные действия — проверяйте только галочки, не запускайте их на основном компьютере.
7. На «Диске» сравните занято/свободно/всего с выбранным накопителем. Цвет не обозначает здоровье диска. Меняйте накопитель; при недоступности ожидается «Нет данных». Проверьте ПКМ на карте и полное имя маленького блока в подсказке и под картой.
8. В пустой истории нет примерных данных. Анализы не попадают в график очисток. Новые записи разделяют удалённые данные и карантин, старые с неизвестным методом не классифицируются.
9. Откройте диагностический экспорт, проверьте скрытые пути и отмените сохранение. Проверка обновлений обращается к GitHub только по кнопке; автоматической установки нет.
10. Проверьте небольшое окно, реальный масштаб Windows 100/125/150/200%, Tab/Shift+Tab, Enter, Esc, Ctrl+K и длинные подписи. Запишите непонятные места и лишние клики.

Не включайте автоочистку, автозапуск или очистку настоящего Temp ради проверки интерфейса. EXE не подписан; не отключайте защиту Windows.

## 🇷🇺 Отдельно: подготовленная Windows VM

Согласуйте реальные действия с владельцем VM. Создайте снимок VM и подготовьте только одноразовые данные в тестовом профиле. Настоящие файлы и настройки Windows не относятся к изолированным автоматическим тестам.

| Сценарий | Ожидается |
|---|---|
| Ручной Temp | Доступные файлы любого возраста и расширения; закрыть приложения, проверить карантин |
| Исключённый, занятый или непроверяемый файл | Сохранён; указана причина пропуска |
| Данные вне Temp, ссылки, cloud placeholders, исключения, runtime .NET | Защищены. Документы и настройки, вручную оставленные внутри Temp, требуют собственного исключения |
| Поддерживаемое приложение работает | Связанная категория пропущена; процесс не завершается |
| Замена/изменение объекта после подтверждённого анализа | Файл не меняется; новые объекты не захватываются |
| Карантин | Перенос отражён без обещания освобождённого места |
| Карантин на другом диске | Исходный файл сохранён, причина объяснена |
| Безвозвратный режим | Отдельное подтверждение; восстановления нет |
| Отмена очистки | Уже выполненные действия учтены; остальные файлы сохранены |
| Восстановление с конфликтом имени | Существующий файл не перезаписан, возвращённая копия имеет отдельное имя; содержимое сверено |
| Поиск карантина | Имя/путь/категория и фильтр незавершённых перемещений; восстановление относится ко всей сессии |
| Прерывание процесса или повреждение журнала | Остатки сохранены; небезопасное продолжение блокируется |
| Нехватка места | Следующие действия остановлены; журнал и исходные файлы сохранены |
| 3000 одноразовых файлов в карантине | Корректная отмена и продолжение удаления; время и характеристики диска зафиксированы |
| Автоочистка | Только при открытом Cleaner; создание и изменение старше 24 часов, документы/настройки сохраняются, всегда карантин; изначально выключена |
| Полноэкранное активное окно, диалог, другая операция | Автоочистка ждёт; закрытый Cleaner ничего не выполняет |
| Автозапуск | Собственная запись Cleaner в HKCU включается/удаляется; после переноса ZIP включается из новой копии |
| Твики и системные действия | Доступны по режиму и ограничениям build; непроверенный эффект обозначен явно. В VM сверить реальный эффект и откат |
| Службы | Экспертный режим; критические защищены. Твик меняет тип запуска без принудительной остановки; полный эффект после перезагрузки. Сторонние изменения при откате сохраняются |
| Старые журналы Cleaner | Настройки → место на диске. Удалены только проверенные журналы прошлых дней; текущий журнал, карантин, настройки и данные восстановления сохранены |
| Старые настройки, история и карантин | Читаются без потери данных; неполные старые системные бэкапы сохранены для ручной проверки |

Подмена объектов, завершение процесса, заполнение диска и повреждение журналов проверяются только в VM на одноразовых данных. Изолированные модели ошибок не заменяют испытания реального процесса и диска.

Матрица: Windows 10 22H2 x64 и Windows 11 24H2/25H2/26H2 x64. Все строки требуют отдельного подтверждения. Перед стабильным выпуском минимум **пять неопытных пользователей** должны самостоятельно пройти анализ → объяснение → подтверждение → отчёт → восстановление.

## 🇬🇧 On an ordinary PC: explore without cleanup

1. Extract the complete win-x64 ZIP with `Data` beside `Cleaner.App.exe`; .NET is included. Never run old and new copies together.
2. Read the first-launch notice. Choose **Understood, continue** to explore; creating a restore point is a separate system action.
3. Check Windows RU/EN, manual override and system-language mode after restarting. Existing preferences survive updates; other languages fall back to English.
4. Confirm `1.1.0-beta.7.1` in the header and What's new, with previous versions preserved. History and Settings are separate from primary tools.
5. Check simple mode, recommended temporary files and the gaming profile. Expand consequences, requirements and recovery information. Simple mode exposes optional caches and histories without selecting them automatically. Expert mode adds system actions with warnings; do not apply them during UI exploration.
6. Use **Check computer**, review counts, skipped reasons and clickable category bars, then cancel another analysis. Analysis must never move or delete files.

Check **Soft**, **Standard** and **Deep** names. All selects categories available in the current mode; None clears them without changing files. Bulk selection clears the highlighted preset; choosing a preset again restores its selection. All, None and preset selection are disabled during analysis. Expert-mode All includes system actions: inspect checkboxes only, never apply them on your main PC.
7. Compare Disk used/free/total with the selected drive. Color is occupancy, not health. Switch drives; unavailable data shows **No data**. Check map context menus and full names of tiny cells.
8. Empty history has no sample records. Analyses do not enter the cleanup chart; deleted and quarantined sizes stay separate. Unknown legacy methods remain unclassified.
9. Review diagnostic export with private details hidden, then cancel saving. The update button queries GitHub on request and never installs an update.
10. Check a small window, actual Windows scaling at 100/125/150/200%, keyboard navigation, Ctrl+K and long labels. Record confusing steps and unnecessary clicks.

Keep automatic cleanup and startup disabled during UI exploration. The EXE is unsigned; do not disable Windows protection.

## 🇬🇧 Authorized Windows VM: disposable data only

Obtain the VM owner's agreement, take a snapshot and use a disposable profile. Verify old/fresh/busy/excluded files, protected saves/configs/documents/links/cloud placeholders, running application protection, object replacement after analysis and cancellation. Manual Temp cleanup includes accessible files of any age and extension; close applications first. Automatic cleanup still requires creation and modification ages of at least 24 hours and preserves documents/configurations. Add exclusions for valuable files stored inside Temp.

Test quarantine versus separately confirmed permanent deletion, same-drive space accounting, cross-drive skips, restoration with name collisions and identical contents, file search, interrupted moves, damaged journals, low disk space and a large disposable session. Process termination, journal corruption and disk exhaustion belong in a prepared VM, never on valuable data.

Automatic cleanup quarantines eligible user temporary files only while Cleaner is open. It waits during a fullscreen foreground window, dialogs and other operations. Startup changes only Cleaner's own HKCU registration. Both options start disabled. Re-enable startup from the new copy after moving the app.

Check mode-dependent access and Windows build constraints. Unverified actions remain labelled unverified. Test actual effects and rollback in the VM only. Service tweaks change startup type without forcibly stopping services; restart to verify the full effect. Critical services remain protected and rollback must preserve outside changes. In Settings, test old Cleaner log cleanup: current logs, settings, quarantine and recovery data must survive. Verify migration of old preferences, history and quarantine; incomplete legacy system backups must be retained for manual review. Isolated fault simulations do not validate real process crashes or hardware behavior.

Target matrix: Windows 10 22H2 x64 and Windows 11 24H2/25H2/26H2 x64; all require explicit validation. A stable release also needs at least **five nontechnical testers** completing analysis → explanation → confirmation → report → recovery independently.

## 📣 Отзыв / feedback

[Issues](https://github.com/Sking32/Cleaner/issues/new/choose) — ошибки и пожелания / bugs and requests. [Discussions](https://github.com/Sking32/Cleaner/discussions) — вопросы и впечатления / questions and impressions.

Укажите / include: Cleaner version, Windows version/build, x64, RU/EN, display scale, reproduction steps, expected and actual result. Review diagnostic previews and screenshots before sharing. Не отправляйте личный профиль, карантин или резервные копии реестра / never attach your personal profile, quarantine contents or registry backups.
