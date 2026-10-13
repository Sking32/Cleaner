# Совместимость Cleaner 1.1.0-beta.7.1 / Compatibility

## Целевые системы / Target platforms

| Система / platform | Архитектура | Реальные испытания beta.7.1 / real validation |
|---|---|---|
| Windows 10 22H2 | x64 | Не выполнены / pending |
| Windows 11 24H2 | x64 | Не выполнены / pending |
| Windows 11 25H2 | x64 | Не выполнены / pending |
| Windows 11 26H2 | x64 | Не выполнены / pending |
| ARM64, Insider | — | Вне выпуска / outside release |

В beta.7.1 отсутствие испытаний больше не блокирует действие само по себе: пользователь видит «Не проверено». Обычный режим показывает менее рискованные операции, экспертный добавляет системные. Явные ограничения build и защита критических служб действуют в обоих режимах. Доступность не подтверждает совместимость или полезность действия. Настройки разных редакций Windows могут игнорироваться системой.

Beta.7 no longer blocks an action solely because it is unverified: the UI labels it accordingly. Simple mode exposes lower-risk operations; expert mode adds system actions. Explicit build limits and critical service protection apply in both modes. Availability does not prove compatibility or benefit; some editions may ignore a setting.

Для подтверждения каждой строки нужны согласованные испытания в VM: build/редакция, RU/EN, DPI, очистка и восстановление, фактический эффект твиков и откат, прерывание, миграция старых настроек и журналов. Изолированные тесты и рендеры WPF этого не заменяют.

Validate each platform in an authorized VM: record build/edition, language, DPI, cleanup and recovery, actual tweak effects and rollback, interruptions and migrations. Isolated tests and WPF renders are insufficient to mark a Windows build validated.

## Каталог твиков / Tweak catalog

Таблица отражает метаданные, а не результаты реального испытания. «safe» не означает нулевой риск или гарантированное ускорение. / This is catalog metadata, not real-system validation. “safe” does not mean zero risk or guaranteed performance gains.

| Ключ / key | Режим / mode | Риск / risk | Min build | Max build | Проверенные build / verified | Перезапуск / restart |
|---|---|---|---|---|---|---|
| VisualFx | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| Transparency | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| StartupDelay | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| MenuDelay | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| BackgroundApps | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| SysMain | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| Prefetch | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| AutoMaintenance | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| DefenderScanLimit | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| WerSvc | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| FastStartup | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| ReservedStorage | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| EventLogSize | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| SearchIndexing | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| PowerThrottling | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| Telemetry | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| AdvertisingId | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| ActivityHistory | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| Cortana | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| XboxGameBar | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| BingSearch | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| LockScreenAds | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| Copilot | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| Recall | Обычный / simple | safe | 26100 | — | Нет / none | Нет / no |
| Feedback | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| Location | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| Camera | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| Microphone | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| DiagnosticData | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| InkingTyping | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| SpeechRecognition | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| UpdateP2P | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| ClassicContext | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| Widgets | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| SearchHighlights | Обычный / simple | safe | 22000 | — | Нет / none | Нет / no |
| StartRecommendations | Обычный / simple | safe | 22000 | — | Нет / none | Нет / no |
| TaskbarAlign | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| SearchBox | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| ChatIcon | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| TaskView | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| EndTask | Обычный / simple | safe | 22000 | — | Нет / none | Да / yes |
| NetworkThrottle | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| TcpAutotuning | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| Nagle | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| QosReserved | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| NetBIOS | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| IPv6 | Экспертный / expert | danger | 19045 | — | Нет / none | Да / yes |
| WindowsUpdateP2P | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| StorageSense | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| Tips | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| ConsumerFeatures | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| ScheduledDefrag | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| DeliveryOptimization | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| OneDriveAutoStart | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| EdgePreload | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| WindowsUpdatePause | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| GameMode | Обычный / simple | safe | 19045 | — | Нет / none | Нет / no |
| HAGS | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| PowerPlan | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| FullscreenOpts | Экспертный / expert | caution | 19045 | — | Нет / none | Нет / no |
| MPO | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
| GamePowerThrottling | Экспертный / expert | caution | 19045 | — | Нет / none | Да / yes |
