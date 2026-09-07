# RoomForge — Unity Procedural Dungeon Generator

## Что это за проект
Unity Editor Tool + Runtime библиотека для процедурной генерации 2D уровней (roguelike/dungeon-crawler).
Разработчик настраивает правила генерации через визуальный редактор в Unity Editor.
Генерация происходит **в runtime** во время игры — каждый раз новый уровень.

## Целевая аудитория
Indie-разработчики Unity, делающие roguelike / dungeon-crawler. Новички и средний уровень.
Конкурент: Dungeon Architect ($60+, сложный). Наш тезис: делает одно, но просто.

## Стек
- Unity 6.3 LTS, 2D URP
- C#, .NET Standard 2.1
- Editor UI: IMGUI (EditorWindow)
- Без внешних зависимостей

---

## Архитектура (два слоя)

### Editor Layer (работает в Unity Editor)
- `RoomForgeEditorWindow` — главное окно (Window → RoomForge)
- `DungeonConfigSO` — ScriptableObject с конфигурацией правил, сохраняется в Assets
- Визуальный граф правил на IMGUI: узлы = типы комнат, связи = допустимые соединения
- Preview генерирует схему уровня прямо в редакторе без запуска игры

### Runtime Layer (работает во время игры)
```
DungeonGenerator (MonoBehaviour)
  ├── GraphBuilder       — строит граф комнат по правилам из DungeonConfigSO
  ├── ILayoutSolver      — интерфейс размещения комнат (реализации: GridLayoutSolver, CompactLayoutSolver)
  ├── RoomPlacer         — инстанциирует prefab-комнаты и коридоры в сцене
  └── Validator          — проверяет проходимость, перегенерирует при провале
```

**Важно:** вся генерация происходит в runtime. Разработчик вызывает `Generate()` в своём коде
(например в `Start()` или при переходе на новый уровень) — и получает готовый уровень в сцене.

---

## Алгоритм генерации

**LayoutSolver:** выбирается через `DungeonConfigSO.layoutSolverType`, две реализации:

- `GridLayoutSolver` (default) — grid-based с random walk. Все комнаты на равномерной сетке
  (шаг = `gridCellSize`, одна величина под самый большой префаб в конфиге) — исключает перекрытия
  автоматически по построению (разные ячейки ⇒ разные позиции).
- `CompactLayoutSolver` — та же топология по 4 кардинальным направлениям (совместима с
  фиксированными `DoorAnchor_N/S/E/W` на префабах), но шаг между конкретной парой комната-сосед
  берётся из их реального `RoomDefinition.size`, а не из общего `gridCellSize` — маленькие комнаты
  не растягиваются на месте большого босс-рума. Расплата: переменный шаг не гарантирует отсутствие
  перекрытий между независимыми ветками так же строго, как равномерная сетка (у разных
  индексов ячеек больше нет общего масштаба) — после раскладки идёт ограниченный проход
  разрешения наложений (итеративная раздвижка AABB), а финальную защиту от редких нераспутанных
  случаев даёт уже существующий retry-цикл `DungeonGenerator` (`Validator` + `maxGenerationAttempts`).

**Расширяемость:** реализован через интерфейс `ILayoutSolver`.
В будущем можно добавить BSP, backtracking и другие алгоритмы не переписывая архитектуру.
Общая для обоих солверов логика выбора/учёта ячеек (`GridTopology`, internal) — какая
соседняя ячейка свободнее, схождение веток по `mergeChance` — вынесена в общий модуль, чтобы
не дублироваться между реализациями.

**Коридоры:** динамическая генерация в runtime.
Между двумя door anchors прокладывается L-образный или прямой тайловый коридор.
Разработчик не делает коридоры вручную — плагин генерирует их автоматически.

---

## Типы комнат
- `StartRoom` — единственная, стартовая
- `EndRoom` — единственная, финальная / boss
- `NormalRoom` — обычная, может повторяться, задаётся вес (weight)
- `SpecialRoom` — treasure, shop и т.д. (настраиваемый тег через `RoomEntry.specialTag`)

`specialTag` — подтип `SpecialRoom` (например "treasure"/"shop"). `BranchRule.deadEndTag` задаёт,
какой тег запросить для комнаты-листа тупиковой ветки, растущей из данного `RoomType`; пусто = без
фильтрации, кандидат выбирается из всех `SpecialRoom`-префабов. `RoomPlacer.PickPrefab` фильтрует
кандидатов по `specialTag`, если запрошенный тег не находит совпадений — откатывается на весь пул
`SpecialRoom`-префабов (с предупреждением в консоли).

---

## Ключевые классы

| Класс | Описание |
|---|---|
| `DungeonGenerator` | MonoBehaviour, вешается на GameObject в сцене |
| `DungeonConfigSO` | ScriptableObject с правилами генерации |
| `RoomDefinition` | Компонент на prefab-комнате (тип + door anchors) |
| `RoomInstance` | Runtime-объект комнаты с метаданными |
| `GraphBuilder` | Строит граф комнат по правилам |
| `ILayoutSolver` | Интерфейс алгоритма размещения |
| `GridLayoutSolver` | Реализация ILayoutSolver (равномерная сетка + random walk) |
| `CompactLayoutSolver` | Реализация ILayoutSolver (тот же граф, шаг по реальному размеру комнат) |
| `RoomPlacer` | Инстанциирует prefab-ы в сцене |
| `Validator` | Проверяет проходимость уровня |
| `BackgroundFiller` | Заполняет фоновым тайлом (Unity Tilemap) пространство вокруг размещённых комнат и коридоров + отступ; работает по финальным world-space `Position`/`Size`, не зависит от того, какой `ILayoutSolver` их посчитал |
| `SpawnPoint` | Маркер-компонент на дочернем объекте prefab-комнаты — точка для спавна контента (враги/награды) |

**Namespace:** `RoomForge`
**Папка:** `Assets/RoomForge/`

---

## Публичный API

```csharp
// Основные методы
DungeonGenerator.Generate()            // генерация уровня в runtime
DungeonGenerator.Regenerate()          // очистка + повторная генерация

// Events
DungeonGenerator.OnGenerationComplete  // вызывается после успешной генерации
DungeonGenerator.OnRoomPlaced          // Action<RoomInstance>, вызывается на каждую комнату
                                        // (после PlaceRooms+PlaceCorridors, до OnGenerationComplete) —
                                        // точка расширения для спавна врагов/наград

// Данные после генерации
DungeonGenerator.Rooms                 // List<RoomInstance> — все комнаты
DungeonGenerator.StartRoom             // RoomInstance — стартовая комната
DungeonGenerator.EndRoom               // RoomInstance — финальная комната

// RoomInstance
RoomInstance.Type                      // тип комнаты (StartRoom, NormalRoom и т.д.)
RoomInstance.Neighbors                 // List<RoomInstance> — соседние комнаты
RoomInstance.Position                  // Vector2 — позиция в мире
RoomInstance.SpecialTag                // string — запрошенный подтип SpecialRoom (см. BranchRule.deadEndTag), может быть null
RoomInstance.Definition.SpawnPoints    // List<Transform> — точки для спавна контента в комнате
RoomInstance.GetDirectionTo(neighbor)  // DoorDirection? — направление от комнаты к её соседу;
                                        // вместе с Definition.DoorAnchors даёт разработчику точку
                                        // конкретной двери между двумя комнатами (напр. под locked door)
```

---

## Door Anchors (точки соединения комнат)

На prefab-комнате — дочерние GameObject-ы с тегами:
`DoorAnchor_N`, `DoorAnchor_S`, `DoorAnchor_E`, `DoorAnchor_W`

Компонент `RoomDefinition` собирает их автоматически при инициализации.
Плагин использует эти точки для динамической генерации коридоров в runtime.

---

## Spawn Points (точки для контента)

На prefab-комнате — произвольное количество дочерних GameObject-ов с компонентом `SpawnPoint`
(пустой маркер, без логики спавна — сам спавн враги/наград реализует разработчик в своём коде).

`RoomDefinition.SpawnPoints` (`List<Transform>`) собирает их автоматически при инициализации,
аналогично `DoorAnchors`.

Разработчик подписывается на `DungeonGenerator.OnRoomPlaced` и по `RoomInstance.Type`
(и `specialTag`, когда реализован) решает, что заспавнить в `room.Definition.SpawnPoints`.

---

## Нефункциональные требования
- Генерация уровня до 30 комнат — не более 100ms.
  Измерено 2026-08-12 (`PerformanceProfilingTests`, реальный `DungeonConfig.asset` и реальные префабы,
  20 прогонов): 30 комнат без ветвления — median 4.59ms, max 7.26ms. Запас ×14–22, NFR выполняется
  с большим запасом.
- Новый пользователь получает результат за 15 минут по README
- Поддержка Unity 2021.3 LTS и выше (целевая: Unity 6.3 LTS)
- Без внешних зависимостей — чистый C# + Unity API

---

## UX-принципы редактора (IMGUI)
1. Результат виден сразу — Preview обновляется без кнопки сохранения
2. Без скрытых настроек — всё видно в одном окне
3. Понятные ошибки — конкретные сообщения при конфликте правил
4. Без чтения документации — базовый сценарий интуитивен

---

## Структура папок проекта

```
Assets/
  RoomForge/
    Editor/
      RoomForgeEditorWindow.cs
    Runtime/
      Core/
        DungeonGenerator.cs
        DungeonConfigSO.cs
        RoomDefinition.cs
        RoomInstance.cs
      Generation/
        GraphBuilder.cs
        ILayoutSolver.cs
        GridLayoutSolver.cs
        RoomPlacer.cs
        CorridorGenerator.cs
        Validator.cs
    Prefabs/
      Rooms/          ← placeholder комнаты
      Corridors/      ← placeholder коридоры
```

---

## Текущий статус

- [x] ТЗ сформировано
- [x] Репозиторий создан (GitHub: KishTeo/room-forge)
- [x] Unity 6.3 LTS проект создан (2D URP)
- [x] CLAUDE.md создан
- [x] Структура папок Assets/RoomForge/
- [x] Namespace и assembly definition
- [x] DungeonConfigSO
- [x] RoomDefinition
- [x] RoomInstance
- [x] GraphBuilder
- [x] ILayoutSolver + GridLayoutSolver
- [x] CorridorGenerator
- [x] RoomPlacer
- [x] Validator — проверяет связность графа, отсутствие пересечений комнат друг с другом и пересечений коридоров с комнатами; DungeonGenerator перегенерирует при провале (retry, maxGenerationAttempts)
- [x] Ветвления графа — GraphBuilder строит критический путь + случайные тупиковые ветки (BranchRule per RoomType), опциональное схождение веток обратно в сетку (mergeChance, решается в GridLayoutSolver по факту grid-adjacency)
- [x] RoomForgeEditorWindow (IMGUI) — все 4 фазы: пикер DungeonConfigSO, список комнат, визуальный граф связей (4 узла = RoomType, клик-клик тумблер ConnectionRule, повторный клик = self-loop), поля BranchRule на узлах, настройки генерации; живое превью схемы уровня (GraphBuilder+GridLayoutSolver на фиксированном preview seed, без Instantiate, кнопка Reroll); валидация конфликтов конфига прямо в окне (отсутствие обязательных префабов, некорректные Min/Max/Grid/Corridor Tile Size)
- [x] Placeholder prefab-комнаты — Room.prefab и Corridor.prefab, тестовые
- [x] Spawn Points — компонент SpawnPoint, RoomDefinition.SpawnPoints, событие DungeonGenerator.OnRoomPlaced
- [x] specialTag — RoomPlacer.PickPrefab фильтрует по тегу, BranchRule.deadEndTag запрашивает тег для листа ветки
- [x] Автотесты — EditMode/NUnit на GraphBuilder/GridLayoutSolver/Validator (Assets/RoomForge/Tests/Editor/), все зелёные
- [x] Профилирование NFR — 30 комнат: median 4.59ms, max 7.26ms (см. Нефункциональные требования)
- [x] Второй ILayoutSolver — CompactLayoutSolver (шаг по реальному размеру комнат вместо gridCellSize), выбирается через DungeonConfigSO.layoutSolverType
- [x] Background Fill — опциональное заполнение пространства вокруг комнат/коридоров фоновым Tilemap-тайлом (DungeonConfigSO.fillBackground/backgroundTile/backgroundTileSize/backgroundPadding/backgroundSortingOrder), BackgroundFiller.Fill вызывается в DungeonGenerator.Generate() после PlaceCorridors; работает одинаково для GridLayoutSolver и CompactLayoutSolver
- [ ] README
