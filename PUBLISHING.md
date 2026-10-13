# Публикация beta / Publishing the beta

[Русская инструкция](README.ru.md) · [English guide](README.md)

## Русский: когда черновик уже подготовлен

Для этой beta готовятся ZIP для Windows x64, SHA-256, описание на двух языках и GitHub-формы для ошибок и предложений. Публикация остаётся ручным решением владельца.

1. Войдите в GitHub под аккаунтом Sking32 и откройте https://github.com/Sking32/Cleaner/releases.
2. Найдите `Cleaner 1.1.0-beta.3` с отметкой **Draft**. Пока выпуск в черновике, тестеры его не видят.
3. Нажмите значок карандаша / **Edit**. Проверьте тег `v1.1.0-beta.3`, описание и два вложения: `Cleaner-1.1.0-beta.3-win-x64.zip` и `Cleaner-1.1.0-beta.3-win-x64.zip.sha256`.
4. Оставьте **Set as a pre-release** включённым. Не выбирайте стабильный выпуск. Если хотите ещё подумать, нажмите **Save draft**.
5. Когда решите начать тестирование, нажмите **Publish release** внизу страницы. Другие операции Git, сборка и загрузка файлов к этому моменту уже завершены.
6. Откройте опубликованную страницу в приватном окне браузера, проверьте скачивание ZIP и скопируйте ссылку на конкретный выпуск.
7. Передайте людям ссылку на выпуск и https://github.com/Sking32/Cleaner/issues/new/choose. Для отправки отзывов нужен аккаунт GitHub. Там можно выбрать ошибку или общий отзыв; обе формы принимают русский и английский.

Готовое приглашение:

> Ищу тестеров Cleaner 1.1.0-beta.3 для Windows x64. Это beta: начните с интерфейса, настроек и анализа без очистки. Системные изменения проверяйте в отдельной Windows VM со снимком. Скачать полный ZIP и SHA-256: [вставьте ссылку на выпуск]. Ошибки и идеи: https://github.com/Sking32/Cleaner/issues/new/choose. Укажите версию приложения и Windows; уберите личные данные со скриншотов. Спасибо!

EXE не подписан. Не просите тестеров отключать защиту Windows. Точка восстановления не заменяет копии личных файлов. Системные операции и создание точки ещё требуют ручных проверок в VM. Репозиторий и формы Issues публичные: не публикуйте приватные пути, пароли, карантин, бэкапы реестра или непроверенные журналы.

## English: publishing a prepared draft

The beta uses a Windows x64 ZIP, SHA-256 checksum, bilingual release notes, and GitHub forms for bugs and feedback. The owner makes the final publication decision.

1. Sign in as Sking32 and open https://github.com/Sking32/Cleaner/releases.
2. Find the `Cleaner 1.1.0-beta.3` **Draft**. Testers cannot see it until it is published.
3. Click the pencil / **Edit**. Check tag `v1.1.0-beta.3`, release notes, and both assets: `Cleaner-1.1.0-beta.3-win-x64.zip` and its `.zip.sha256` file.
4. Keep **Set as a pre-release** selected. Use **Save draft** if you need more time.
5. When ready to invite testers, click **Publish release** at the bottom. Source preparation, packaging, and asset upload should already be complete.
6. Check the published ZIP download in a private browser window, then share the specific release URL.
7. Share https://github.com/Sking32/Cleaner/issues/new/choose for bugs and general feedback. A GitHub account is required to submit either form; English and Russian are welcome.

Invitation text:

> Looking for testers for Cleaner 1.1.0-beta.3 on Windows x64. Start with the interface, settings, and analysis without cleanup. Test system changes only in a disposable Windows VM with a snapshot. Download the complete ZIP and SHA-256: [insert release URL]. Bugs and ideas: https://github.com/Sking32/Cleaner/issues/new/choose. Include the app and Windows versions, and remove personal data from screenshots. Thank you!

The EXE is unsigned. Do not ask testers to disable Windows protection. A restore point is not a personal-file backup. Native system operations and restore-point creation still need manual VM validation. Issues are public; never upload secrets, private paths, quarantine contents, registry backups, or unreviewed logs.

## Следующие beta / Future beta releases

Для исправлений меняйте версию, сохраняйте старые записи в «Что нового?» и добавляйте новую запись RU/EN. Собирайте новый ZIP из коммита, на который указывает новый тег. Не заменяйте файлы уже опубликованной beta незаметно.

For fixes, increment the version, retain previous entries in What's new, and add a new bilingual entry. Build from the commit referenced by the new tag; do not silently replace an existing public beta.

[GitHub: managing releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository)
