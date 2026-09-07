# RoomForge

Процедурный генератор 2D-подземелий для Unity. Настраиваешь правила в визуальном
редакторе — граф типов комнат, шанс ветвления, размеры сетки — и получаешь готовый
уровень в рантайме, каждый раз новый.

Без внешних зависимостей. Unity 2021.3 LTS и новее (2D URP).

## Установка

Скопируй папку `Assets/RoomForge` в свой проект. Плагин самодостаточен, но теги
`DoorAnchor_N` / `_S` / `_E` / `_W` — проектная настройка (Tag Manager), а не часть
ассетов: если копируешь папку в уже существующий проект, а не клонируешь этот
репозиторий целиком, добавь эти 4 тега вручную (Project Settings → Tags and Layers).

## Быстрый старт (15 минут)

1. **Подготовь префабы комнат.** На каждом префабе комнаты повесь компонент
   `RoomDefinition`, укажи тип (`StartRoom` / `NormalRoom` / `EndRoom` / `SpecialRoom`)
   и размер. Дочерними объектами с тегами `DoorAnchor_N` / `_S` / `_E` / `_W` отметь
   точки, где к комнате может пристыковаться коридор — RoomForge находит их сам.
   Добавь **все 4** якоря на каждом префабе, даже если по факту в конкретной раскладке
   какая-то сторона может остаться неиспользованной — на этой стороне должна встать
   заглушка двери (`doorBlockerPrefab`), а RoomForge умеет закрыть только ту сторону,
   на которой есть якорь (см. «Частые проблемы» ниже).
   (Готовый рабочий пример — набор `Assets/RoomForge/Prefabs/Rooms/Space-room_*`,
   собранный на конфиге `Assets/RoomForge/SO/Space.asset`.)
2. **Создай конфиг.** `DungeonConfigSO` (Create → RoomForge → Dungeon Config) — сюда
   добавляешь префабы комнат по типам, задаёшь связи между типами (какой тип может
   соединяться с каким), правила ветвления и размер сетки.
3. **Открой окно редактора.** `Window → RoomForge` — визуальный граф связей, live-превью
   схемы уровня, проверка конфига на ошибки (отсутствующие префабы, некорректные
   размеры) прямо в окне.
4. **Повесь генератор в сцену.** Пустой GameObject → компонент `DungeonGenerator`,
   назначь ему свой `DungeonConfigSO`.
5. **Вызови генерацию из кода:**

   ```csharp
   using RoomForge;
   using UnityEngine;

   public class LevelBootstrap : MonoBehaviour
   {
       [SerializeField] private DungeonGenerator generator;

       private void Start() => generator.Generate();
   }
   ```

Готово — уровень собран в сцене под `Dungeon` дочерним объектом генератора.

## Спавн игрока и контента

```csharp
generator.OnGenerationComplete += () =>
{
    transform.position = generator.StartRoom.Position;
};

generator.OnRoomPlaced += room =>
{
    // room.Type, room.SpecialTag, room.Definition.SpawnPoints — решай, что заспавнить
};
```

`OnRoomPlaced` вызывается на каждую комнату после того, как она и коридоры к ней
размещены, но до `OnGenerationComplete` — самое время расставить врагов/награды по
`room.Definition.SpawnPoints` (маркеры `SpawnPoint` на префабе комнаты).

## Частые проблемы

**У части комнат остаются открытыми неиспользуемые двери.**
`RoomDefinition` собирает якоря только с прямых дочерних объектов, у которых
проставлен тег `DoorAnchor_N/_S/_E/_W`. Если на префабе комнаты не хватает якоря
хотя бы с одной стороны — эту сторону RoomForge не закроет заглушкой (и не выдаст
предупреждение в консоль, просто молча пропустит). Проверка: у каждого префаба
комнаты в конфиге должно быть ровно 4 дочерних объекта с этими тегами, независимо
от того, какие стороны реально задействуются в конкретной раскладке.

**`OnGenerationComplete` / `OnRoomPlaced` как будто не срабатывают** (игрок не
телепортируется в старт-рум, контент не спавнится, но ошибок в консоли нет).
Почти всегда причина — пустая ссылка на `DungeonGenerator` в инспекторе у
подписывающегося компонента: подписка на `null` ничего не делает и не логирует
ошибку. Проверь именно в открытой сцене (не в файле на диске), что поле реально
заполнено, и сохрани сцену — несохранённые правки инспектора в этом месте легко
разъезжаются с тем, что лежит на диске.

## Публичный API

```csharp
DungeonGenerator.Generate()            // генерация уровня
DungeonGenerator.Regenerate()          // очистка + повторная генерация

DungeonGenerator.OnGenerationComplete  // Action, после успешной генерации
DungeonGenerator.OnRoomPlaced          // Action<RoomInstance>, на каждую комнату

DungeonGenerator.Rooms                 // List<RoomInstance>
DungeonGenerator.StartRoom             // RoomInstance
DungeonGenerator.EndRoom               // RoomInstance

RoomInstance.Type                      // RoomType
RoomInstance.Neighbors                 // List<RoomInstance>
RoomInstance.Position                  // Vector2, мировые координаты
RoomInstance.SpecialTag                // string, подтип SpecialRoom (может быть null)
RoomInstance.Definition.SpawnPoints    // List<Transform>
RoomInstance.GetDirectionTo(neighbor)  // DoorDirection?
```

## Два алгоритма раскладки

Выбираются через `DungeonConfigSO.layoutSolverType`:

- **Grid** (по умолчанию) — равномерная сетка, шаг = самый большой префаб в конфиге.
  Перекрытия исключены по построению.
- **Compact** — та же топология связей, но шаг между конкретной парой комнат берётся
  из их реального размера — маленькие комнаты не растягиваются на месте большого
  босс-рума. Расплата — после раскладки идёт проход разрешения наложений; страховка
  на редкие нераспутанные случаи — встроенный retry в `DungeonGenerator`.

## Структура

```
Assets/RoomForge/
  Editor/       — окно редактора (Window → RoomForge)
  Runtime/
    Core/       — DungeonGenerator, DungeonConfigSO, RoomDefinition, RoomInstance
    Generation/ — GraphBuilder, ILayoutSolver (Grid/Compact), RoomPlacer, Validator, BackgroundFiller
  Prefabs/      — комплект комнат/коридоров Space (готовый пример)
  SO/           — Space.asset, пример DungeonConfigSO
  Sprites/
```

## Требования к производительности

30 комнат без ветвления генерируются медианно за ~5мс (запас ×14 к целевому лимиту
100мс на реалистичном железе).
