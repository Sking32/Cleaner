# Публикация тестовой версии / Publishing a test release

[Русская инструкция для пользователей](README.ru.md) · [English user guide](README.md)

## Русский: рекомендуемый путь

Используйте **GitHub Releases → Pre-release** в [существующем репозитории Cleaner](https://github.com/Sking32/Cleaner). Это даёт одну страницу скачивания, историю версий и обратную связь через Issues. Отметка beta не означает, что GitHub проверил безопасность приложения. Сам GitHub не приведёт тестировщиков: после небольшой проверки друзьями делитесь ссылкой на релиз в сообществах Windows, где разрешены такие публикации.

### Подготовить пакет

1. Просмотреть изменения и зафиксировать их вручную. Версия в `Directory.Build.props`, README и первая запись `Data/releases.json` должны совпадать.
2. Выполнить сборку и изолированные тесты по `DEVELOPMENT.md`. Нативные действия проверить отдельно в одноразовой VM: первый запуск, UAC (подтверждение и отказ), реально включённая/отключённая защита системы, создание и проверка точки, очистка с возвратом файлов из карантина. Автотесты с заглушками этого не подтверждают.
3. Выбрать собственную лицензию проекта и добавить `LICENSE`. Здесь лицензия за автора не назначалась. Проверить, что в репозитории нет пользовательских данных, ключей и журналов. `.test-sandbox`, `.recovery` и `artifacts` не загружать как исходники.
4. После фиксации исходников запустить `powershell -NoProfile -File .\build-preview.ps1`. Перед публикацией пересобрать пакет из того же коммита, на который будет указывать тег релиза.
5. Передавать готовый ZIP и его `.zip.sha256`. README на двух языках и уведомления о зависимостях включены в ZIP. Не выкладывать EXE отдельно от `Data` и не заменять файл уже переданной beta — для исправлений выпускать следующую версию.

### Создать выпуск вручную

1. Отправить проверенный коммит в свой репозиторий. Если тест должен быть публичным, убедиться, что людям доступна страница проекта; приватный репозиторий требует настроенного доступа.
2. Открыть **Releases → Draft a new release**. Задать новый тег `v1.1.0-beta.3` на проверенном коммите и название `Cleaner 1.1.0-beta.3 — public test`.
3. В описание вставить `RELEASE_NOTES.md`, указать x64, тестовый статус и известные ограничения.
4. Прикрепить ZIP и `.zip.sha256`, отметить **Set as a pre-release**. Сначала сохранить черновик и проверить файлы и ссылки, затем опубликовать.
5. Передать ссылку на конкретный релиз. В Assets люди выбирают готовый `Cleaner-…-win-x64.zip`, а не “Source code”. Попросить начать с интерфейса и анализа и использовать Issues для ошибок.

Как устроены выпуски и загрузка файлов: [GitHub — управление Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository), [GitHub — о Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

### После первых отзывов

Сначала исправлять сбои запуска, ошибки восстановления и потери данных. Для каждого нового пакета менять версию, добавлять RU/EN запись в историю, повторять подходящие проверки и прикладывать новую контрольную сумму. Не обещать подтверждённую совместимость с той сборкой Windows, на которой тест ещё не проводился. Подписывание EXE и установщик можно подготовить после стабилизации beta. Автоматическая публичная публикация в этом этапе не настроена.

## English: recommended workflow

Use **GitHub Releases → Pre-release** in the [existing Cleaner repository](https://github.com/Sking32/Cleaner). It gives testers a download page, release history and Issues for feedback. A beta label is not a security review by GitHub. Start with a small group; then share the release link in Windows communities that allow testing invitations.

### Prepare

1. Review and commit the changes manually. Keep the application version, READMEs and first release-catalog entry consistent.
2. Build and run isolated tests. Separately test native behavior in a disposable VM: first launch, UAC approval/cancellation, System Protection on/off, actual point creation and verification, and cleanup followed by quarantine recovery. Mock tests cannot establish native compatibility.
3. Choose a project license and add `LICENSE`; this work does not choose one on the author's behalf. Check the repository for personal data and secrets. Do not upload `.test-sandbox`, `.recovery` or `artifacts` as source files.
4. From the final committed source run `powershell -NoProfile -File .\build-preview.ps1`. Rebuild against the same commit that the release tag will reference.
5. Distribute the complete ZIP and `.zip.sha256`. Both READMEs and dependency notices are included. Do not separate the EXE from `Data` or silently replace an already distributed beta; publish a new version for fixes.

### Publish manually

1. Push the reviewed commit to your repository and make sure intended testers can access it.
2. Open **Releases → Draft a new release**. Create `v1.1.0-beta.3` for the reviewed commit and use `Cleaner 1.1.0-beta.3 — public test` as the title.
3. Paste `RELEASE_NOTES.md` into the description and attach the ZIP plus `.zip.sha256`.
4. Select **Set as a pre-release**. Save and inspect a draft first; then publish.
5. Share the specific release link. Ask testers to use the ready-made ZIP under Assets, start with interface checks and analysis, and report bugs through Issues.

Official instructions: [Managing GitHub Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository), [About Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/about-releases).

Prioritize launch failures, recovery failures and data loss. Each later package needs a new version, bilingual release entry, relevant checks and checksum. Only claim Windows compatibility that was actually tested. Code signing and an installer can follow after beta stabilization. No release is published automatically by this change.
