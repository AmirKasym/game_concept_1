# Контекст проекта

Дата: 2026-09-08. Новый проект в пустой рабочей копии `concept_1`.

## Подтверждено файлами

- Версия в `ProjectSettings/ProjectVersion.txt`: Unity 6000.0.62f1.
- Manifest: URP 17.0.3, Input System 1.11.2, встроенные модули IMGUI, Physics, ImageConversion. Lock-файл должен создать Unity при первом разрешении зависимостей.
- `ProjectSettings/ProjectSettings.asset`: начальные Player Settings, linear color, Input System.
- `Assets/TradeWinds/Core/ShipSimulation.cs`: обычный C#, владелец состояния судна, воспроизводимый fixed-step. Сетевого транспорта нет.
- `Runtime/ShipController.cs`: привязка модели к Unity, препятствия и визуальная качка.
- `Runtime/DeckPlayer.cs`: ввод, локальная палуба, штурвал, камеры и пауза.
- `Runtime/PrototypeWorld.cs`: единственная точка сборки мира из примитивов и mesh; освобождение созданных материалов/мешей при уничтожении.
- `Runtime/PrototypeHud.cs`: русский отладочный IMGUI-интерфейс.
- `Shaders/CoastalSea.shader`: URP-вода с двумя синусами, совпадающими с C#.
- `Editor/PrototypeSetup.cs`: при первом открытии создаёт URP assets и FirstVoyage; сохраняет существующие сцены. Меню подготовки и Windows-сборки.
- Assembly definitions пока нет: стандартные Assembly-CSharp и Assembly-CSharp-Editor. Runtime не импортирует UnityEditor.
- `Tests` и `Tools` вне Assets: самостоятельные тесты C#-модели на Windows.

## Инструменты и неизвестное

Unity Workbench доступен как набор навыков Codex. Unity MCP tools не обнаружены. Unity Hub и Unity CLI установлены; список установленных редакторов был пуст перед запуском установки. Компиляция Unity, импорт пакетов, визуальная проверка и Windows-билд пока не подтверждены.

Сцена и URP assets генерируются средствами Editor; до первого импорта их нет в репозитории. `.meta` исходных скриптов и шейдера сохранены, GUID уникальны.

## Направление работы

Основа — два документа в `Docs/Source`. Текущий объём: этап 1, одиночное ощущение корабля. Не выдавать эту сцену за сетевой вертикальный срез. Следом — проверка управления, затем хост/клиент и один переносимый ящик. Подробности: `Docs/PrototypeScope.md`, результаты: `Docs/Validation.md`.
