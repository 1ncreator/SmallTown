# Small Town — живой миниатюрный город (Unity 6, URP)

Интерактивная песочница: жители, транспорт, погода, река и свет живут в одной детерминированной симуляции.
Всё (меши, материалы, сцена, настройки URP) генерируется кодом.

## Запуск
1. Unity Hub → **Add → Add project from disk** → `E:\UnityHub\Projects\SmallTown` (редактор 6000.5.10f1).
2. Откройте сцену `Assets/_Game/Scenes/SmallTown.unity` и нажмите **Play**.
   Игра стартует и из пустой сцены: её поднимает `[RuntimeInitializeOnLoadMethod]`.
3. Пересобрать сгенерированные ассеты: меню **Tools → Small Town → Build Everything** или в batchmode:
   `Unity.exe -batchmode -quit -projectPath <path> -executeMethod SmallTown.Editor.ProjectBuilder.BuildEverything -logFile build.log`

## Управление
- Перетаскивание: панорама. Колесо, щипок или `+`/`−`: масштаб. Правая кнопка: поворот. Стрелки: движение. `0`: стандартный вид.
- `Пробел`: пауза. `Z`: отменить. `C`: кинорежим. `/`: строка команд. `?` (Shift+/) или `F1`: помощь. `Esc`: закрыть или очистить.
- В строке команд `↑`/`↓` листают историю.
- Клик по жителю или машине показывает, кто это, что делает и куда идёт (с линией маршрута).
  Клик по зданию, мосту или подписи открывает действия для этого места.

## Архитектура
| Сборка | Папки | Что внутри |
|---|---|---|
| `SmallTown.Runtime.Simulation` (без UnityEngine) | `Scripts/Simulation`, `Generation`, `Commands`, `Utils` | генератор города, графы, A*, жители, транспорт, события, снимки, парсер |
| `SmallTown.Runtime.View` | `Scripts/View`, `Core`, `Camera`, `UI` | процедурные меши, GPU instancing, окружение, UI, камера, бутстрап |
| `SmallTown.Editor` | `Editor` | ProjectBuilder (URP, SSAO, пост-обработка, материалы, сцена, Build Settings) |
| `SmallTown.Tests.EditMode` / `PlayMode` | `Tests` | 122 EditMode-теста, PlayMode smoke-тест, съёмка скриншотов |

## Где крутить параметры
- `Assets/_Game/Configs/SimSettings.asset`: seed, размер города (кварталы и ряды), число жителей и машин, длина суток, стартовый час, скорости.
- `Assets/_Game/Configs/CommandGrammar.json`: словарь команд (синонимы RU/EN, числа, единицы).
- `SimConfig.cs`: ширина дорог, тротуаров, реки и набережных.

## Тесты
```
Unity.exe -batchmode -projectPath <path> -runTests -testPlatform EditMode -testResults results_edit.xml
Unity.exe -batchmode -projectPath <path> -runTests -testPlatform PlayMode -testResults results_play.xml
Unity.exe -batchmode -projectPath <path> -runTests -testPlatform PlayMode -testFilter SmallTown.Tests.PlayMode.ScreenshotCapture
```
Скриншоты сохраняются в `Screenshots/`.
