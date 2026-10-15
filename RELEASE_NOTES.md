# 🎨 Cleaner 1.1.0-rc.1 — Graphite

## 🇷🇺 Русский

Полный редизайн Cleaner для Windows x64. **Это кандидат в релиз для тестирования, а не подтверждённая стабильная версия.**

- 🌗 Тёмная и светлая темы: переключение в настройках без перезапуска, с сохранением выбора.
- 🧭 Верхняя навигация: основные инструменты отдельно от «Истории», «Что нового?» и настроек.
- 🧹 Компактные списки очистки, оптимизаций, автозагрузки и служб; обновлены все девять страниц.
- 📊 Обновлены полукруг заполнения диска, карта папок и история результатов. Карантин учитывается отдельно от освобождённого места.
- 🛡️ Усилен запуск системных утилит и восстановление карантина по проверенным дескрипторам, без перезаписи существующих файлов.
- 🌐 Обновлены справка RU/EN и история версий. Выбор пресетов, предварительный анализ и подтверждения сохранены.

**Проверки:** сборка, изолированные автоматические тесты, рендеры RU/EN в обеих темах, проверка зависимостей и состава архива. Реальная матрица Windows, изменение DPI Windows и испытание с пятью неопытными пользователями ещё не завершены. Работа без ошибок на любом компьютере не обещается.

**Ограничения:** старые элементы карантина с восстановлением на другой том остаются в карантине для ручного восстановления. Не удаляйте такую сессию. Непроверенные системные операции в экспертном режиме могут нарушить работу Windows; испытывайте их только в отдельной VM со снимком. Карантин на том же диске не освобождает место. EXE не подписан; не отключайте защиту Windows ради запуска.

📦 В **Assets** скачайте **Cleaner-1.1.0-rc.1-win-x64.zip**, распакуйте целиком и запустите **Cleaner.App.exe**. Папку **Data** оставьте рядом; .NET включён. SHA-256 необязателен и предназначен для проверки совпадения архива. Архивы **Source code** не являются готовым приложением. Не запускайте старую и новую версии одновременно: профиль и карантин общие.

💬 [Ошибка или предложение](https://github.com/Sking32/Cleaner/issues/new/choose) · [Вопросы и впечатления](https://github.com/Sking32/Cleaner/discussions). Укажите версию, build Windows, язык, масштаб, шаги и результат. Перед отправкой удалите личные данные из скриншотов и отчётов.

## 🇬🇧 English

A complete Cleaner redesign for Windows x64. **This is a release candidate for testing, not a validated stable release.**

- 🌗 Dark and light themes: switch in Settings without restarting; your choice is saved.
- 🧭 Top navigation separates the main tools from History, What’s new? and Settings.
- 🧹 Compact cleanup, optimization, startup and service lists; all nine pages have been redesigned.
- 📊 Refreshed disk occupancy gauge, folder map and cleanup history. Quarantine is counted separately from freed space.
- 🛡️ Hardened system utility execution and handle-based quarantine recovery without overwriting existing files.
- 🌐 Updated RU/EN help and release history. Presets, analysis and confirmation steps are preserved.

**Validation:** builds, isolated automated tests, RU/EN renders in both themes, dependency and archive checks. The real Windows compatibility matrix, Windows DPI changes and trials with five novice users remain outstanding. Error-free operation on every computer is not promised.

**Limitations:** legacy quarantine items restoring to another volume remain quarantined for manual recovery. Keep that session. Unverified Expert-mode system actions can disrupt Windows; test them only in a separate VM with a snapshot. Same-drive quarantine does not free space. The EXE is unsigned; do not disable Windows protection to run it.

📦 Under **Assets**, download **Cleaner-1.1.0-rc.1-win-x64.zip**, extract everything and run **Cleaner.App.exe**. Keep **Data** beside it; .NET is included. SHA-256 is optional and checks archive integrity. **Source code** archives are not the runnable application. Do not run old and new versions simultaneously: they share the same profile and quarantine.

💬 [Report a bug or request a feature](https://github.com/Sking32/Cleaner/issues/new/choose) · [Questions and impressions](https://github.com/Sking32/Cleaner/discussions). Include version, Windows build, language, display scale, steps and results. Remove personal information from screenshots and reports before posting.
