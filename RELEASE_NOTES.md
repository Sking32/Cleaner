# Cleaner 1.1.0-beta.3 — тестирование / beta testing

## Русский

Тестовый выпуск для Windows x64. Скачайте **Cleaner-1.1.0-beta.3-win-x64.zip** и **.zip.sha256** в разделе Assets. Распакуйте архив целиком; папка Data должна оставаться рядом с Cleaner.App.exe. .NET включён в сборку.

В beta входят:

- Обновлённый интерфейс, компактные списки, отдельные настройки, история действий и список версий.
- Анализ без очистки, просмотр результатов и сохранение пользовательских пресетов.
- Усиленные проверки очистки и карантина, обработка ошибок удаления и восстановления, защита важных служб и атомарное сохранение данных.
- Памятка первого запуска с предложением создать точку восстановления и возможностью продолжить знакомство.
- Язык интерфейса Windows по умолчанию и ручной выбор русского или английского.
- Инструкции на двух языках и отдельные формы для ошибок и общего отзыва.

Начните с интерфейса, настроек и анализа без очистки. Реальную очистку, службы, твики и создание точки проверяйте в отдельной Windows VM со снимком. Изолированные автотесты и проверки привязок пройдены; нативная совместимость системных действий ещё проверяется. Точка восстановления не возвращает удалённые личные файлы. Beta не подписана: не отключайте защиту Windows ради запуска.

[Русская инструкция](https://github.com/Sking32/Cleaner/blob/master/README.ru.md) · [Ошибки и предложения](https://github.com/Sking32/Cleaner/issues/new/choose)

## English

A test release for Windows x64. Download **Cleaner-1.1.0-beta.3-win-x64.zip** and its **.zip.sha256** file under Assets. Extract everything and keep Data beside Cleaner.App.exe. The package includes .NET.

This beta includes:

- A refreshed interface, compact lists, separate settings, action history, and version history.
- Analysis without cleanup, result inspection, and user presets.
- Stronger cleanup and quarantine checks, deletion/recovery error handling, critical-service protection, and atomic data persistence.
- First-launch safety guidance with optional restore-point creation.
- Windows display-language detection and manual Russian/English selection.
- Bilingual instructions and separate forms for bugs and general feedback.

Start with the interface, settings, and analysis without cleanup. Use a disposable Windows VM with a snapshot for real cleanup, services, tweaks, and restore-point creation. Isolated automated tests and binding checks passed; native system-action compatibility is still under evaluation. A restore point is not a backup of deleted personal files. The beta is unsigned; do not disable Windows protection to run it.

[English guide](https://github.com/Sking32/Cleaner/blob/master/README.md) · [Bugs and feedback](https://github.com/Sking32/Cleaner/issues/new/choose)

Reports are public. Remove private paths and other personal data from screenshots; do not upload quarantine contents or registry backups.
Отчёты публичные. Уберите личные данные со снимков; не прикладывайте карантин и бэкапы реестра.
