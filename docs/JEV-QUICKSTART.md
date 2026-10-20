# jev — быстрый старт (техническая шпаргалка для другой сессии Claude)

Написано 10.10.2026 в проекте WOM для переноса в **другой проект**. Здесь только техника: что такое
jev, где он стоит, как его вызвать, что он отвечает, как встроить в код и на какие грабли наступали.
Что спрашивать у модели — решает новый проект. Опыт WOM (игровой бот) — в конце, как пример.

## 1. Что это

**jev** — так мы зовём «решающий» (decision) API Ollama: `POST /v1/systemone` (Ollama 0.35+; анонс —
https://ollama.com/blog/ollama-now-supports-jev-style-decision-models). Модель **не пишет текст**. Она
получает произвольный JSON-«состояние» и набор вопросов и возвращает **вероятности** ответов. На
выходе ~4 токена, поэтому ответ быстрый и стабильный.

Три типа вопросов:

| Тип | Что спрашивает | Что возвращает |
|---|---|---|
| `choice` | выбрать один из именованных вариантов | `choice` (имя лучшего) + `probabilities` по всем вариантам |
| `noul` | да / нет | `noul` — вероятность «да», 0…1 |
| `score` | оценка по шкале из N ступеней | `score` — дробное «среднее» по ступеням (0…N−1), `probabilities` по ступеням, `legend` |

У `choice` и `score` есть ещё `confidence` (0…1).

Модель: **`nimble`** (`nimble:latest`, Bespoke Nimble 9B, Q8_0, 9,5 ГБ, возможность `decision`).
Обычные чат-модели (qwen, gemma и т. п.) этим эндпоинтом **не проверялись**.

## 2. Где стоит

- **ПК в локальной сети: `http://192.168.0.2:11434`** — Windows, Ollama 0.40.2, `nimble:latest`. Основная
  машина для опытов. Полный адрес эндпоинта: `http://192.168.0.2:11434/v1/systemone`.
- Мак игрока (Apple M4 Pro, 24 ГБ, Ollama 0.35.1) — тоже работал, но там модели больше нет (09.10.2026).
- ПК бывает выключен: 10.10.2026 запрос ушёл в тайм-аут. **Сначала проверь, жив ли** (раздел 3). Не
  отвечает — попросить пользователя включить ПК / запустить Ollama, не гадать.

**Грабли брандмауэра (Windows).** Ollama слушала `0.0.0.0:11434`, ping шёл, а TCP молча отбрасывался.
Галочка в окне Windows не помогла; помогло правило из PowerShell под администратором:

```powershell
New-NetFirewallRule -DisplayName "Ollama 11434" -Direction Inbound -Protocol TCP -LocalPort 11434 -Action Allow -Profile Any
```

Ollama должна слушать не только localhost: переменная окружения `OLLAMA_HOST=0.0.0.0` на ПК (на нашем
ПК уже так).

## 3. Проверка за 30 секунд

```sh
# жив ли сервер и какая версия (обычный эндпоинт Ollama)
curl -s -m 5 http://192.168.0.2:11434/api/version
# есть ли модель nimble
curl -s -m 5 http://192.168.0.2:11434/api/tags | grep -o '"name":"[^"]*"'
# пробный решающий запрос (первый вызов грузит модель — 5–7 с)
curl -s -m 60 http://192.168.0.2:11434/v1/systemone -d '{
  "model": "nimble",
  "state": {"time": "06:30", "reserve": 180, "income_per_min": 40,
            "east_chain": "broken at node 9", "enemy_units_near_east_chain": 4},
  "questions": {
    "plan": {"type": "choice", "instructions": "What should the commander focus on next minute?",
             "criteria": {"expand": "Extend network to a new point", "defend": "Defend own network and base",
                          "attack": "Attack the enemy"}},
    "repair_chain": {"type": "noul", "instructions": "Should the broken east chain be repaired now?"},
    "aggression": {"type": "score", "instructions": "How aggressive should the army be?",
                   "criteria": ["Hold at base", "Escort network", "Raid", "All-in assault"]}
  }
}'
```

В WOM похожий запрос дал `plan` = `defend` (0,71), `repair_chain` = 0,88 — разумно. Одинаковый ответ
на маке и на ПК до последней цифры.

## 4. Запрос

```json
{
  "model": "nimble",
  "state":     { ... любой JSON: обстановка, числа, списки, строки ... },
  "questions": { "<имя>": <вопрос>, ... }
}
```

Вопросы:

```json
"pick":   {"type": "choice", "instructions": "Which option is best now?",
           "criteria": {"a": "описание варианта a", "b": "описание варианта b"}}

"ok":     {"type": "noul",   "instructions": "Should we do X now?"}

"level":  {"type": "score",  "instructions": "How important is X right now?",
           "criteria": ["not now", "low", "medium", "high", "top priority"]}
```

- `criteria` у `choice` — **объект** «имя → описание»; у `score` — **массив** ступеней снизу вверх.
- Имена вопросов и вариантов — свои; они же ключи в ответе.
- Писать по-английски (модель так точнее), числа округлять, длинные списки сокращать.
- Контекст задачи удобно класть **внутрь `state`** отдельными полями: `"game": "…описание правил…"`,
  `"commander_style": "…"`, `"commander_handbook": ["правило 1", "правило 2", …]`. Отдельного поля
  «system prompt» нет — всё, что модель должна знать, — в `state` и в `instructions`.

## 5. Ответ

Верхний уровень — объект `answers` (так его читает наш парсер; при первом запуске проверь живым
curl'ом, нет ли рядом других полей). Настоящий ответ из журнала WOM (09.10.2026), сокращённо:

```json
{
  "answers": {
    "expand": {
      "type": "score",
      "score": 1.264,
      "legend": {"0": "not now", "1": "low", "2": "medium", "3": "high", "4": "top priority"},
      "probabilities": {"0": 0.023, "1": 0.833, "2": 0.060, "3": 0.026, "4": 0.058},
      "confidence": 0.585
    },
    "focus": {
      "type": "choice",
      "choice": "expand",
      "probabilities": {"scout": 0.100, "expand": 0.773, "defend": 0.048, "assault": 0.019, "save": 0.061},
      "confidence": 0.491
    },
    "assault_now": {"type": "noul", "noul": 0.0068},
    "expand_now":  {"type": "noul", "noul": 0.984}
  }
}
```

Как читать:
- `choice` → `answers.X.choice` (строка) и `answers.X.probabilities[имя]`.
- `noul` → `answers.X.noul` — вероятность «да». Порог выбирать самому (0,5 — не догма).
- `score` → `answers.X.score` от 0 до N−1; в долю: `score / (N − 1)`. `probabilities` — по индексам
  ступеней строками `"0"…"N−1"`.

## 6. Скорость и поведение (замеры WOM)

| Что | ПК 192.168.0.2 | Мак M4 Pro |
|---|---|---|
| Первый вызов (загрузка модели) | 5,6 с | 7,2 с |
| Повтор того же запроса (кэш префикса) | 0,42 с | 0,68 с |
| Новое состояние, 3 вопроса | **0,67 с** | ~2 с |
| Новое состояние, 6 вопросов (~1600 токенов входа) | 1,4 с | — |

- Время растёт с длиной входа и числом вопросов: **меньше вопросов и короче `state` — быстрее**
  (6 → 3 вопроса: 1,4 → 0,67–0,87 с).
- **Параллельно почти не быстрее**: 1 поток — 0,72 запр/с, 3 потока — 0,90 (лучшее, 1,3×), 8 — 0,73.
  Машина упирается в разбор входа. Считать на ~1 запрос в секунду.
- **Детерминизм**: на одной машине одинаковый вход даёт ответ бит в бит. Между машинами совпало в
  нашем примере, но гарантии нет (сборка, квантование). Если в проекте нужна воспроизводимость —
  писать в журнал сам ответ, а не надеяться повторить вызов.
- Модель после простоя выгружается — первый запрос снова медленный. Прогревать одним вызовом.
- Вероятности бывают **очень острые** (0,98 / 0,01). Если ответ превращается в веса — давать основу:
  в WOM `вес = 0,3 + 0,5 · p / p_max`, иначе второстепенное падало в ноль.

## 7. Код

### Python (только стандартная библиотека)

```python
import json, time, urllib.request

JEV = "http://192.168.0.2:11434/v1/systemone"

def jev(state: dict, questions: dict, model: str = "nimble", timeout: float = 120) -> dict:
    body = json.dumps({"model": model, "state": state, "questions": questions}).encode()
    req = urllib.request.Request(JEV, body, {"Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read())["answers"]

t = time.perf_counter()
a = jev({"queue_len": 12, "errors_last_min": 3},
        {"action": {"type": "choice", "instructions": "What to do now?",
                    "criteria": {"wait": "Do nothing", "scale_up": "Add a worker", "alert": "Page a human"}},
         "urgent": {"type": "noul", "instructions": "Is this urgent?"}})
print(a["action"]["choice"], a["action"]["probabilities"], a["urgent"]["noul"], f"{time.perf_counter()-t:.2f} c")
```

Готовый замер в несколько потоков — `tools/jev_bench.py` в репозитории WOM (можно взять как образец).

### C# (.NET 6+, System.Text.Json.Nodes)

```csharp
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

public sealed class JevClient
{
    private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
    private readonly string _url;
    private readonly string _model;

    public JevClient(string baseUrl = "http://192.168.0.2:11434", string model = "nimble")
    {
        _url = baseUrl.TrimEnd('/') + "/v1/systemone";
        _model = model;
    }

    public async Task<JsonNode> AskAsync(JsonObject state, JsonObject questions, CancellationToken ct = default)
    {
        var body = new JsonObject { ["model"] = _model, ["state"] = state, ["questions"] = questions };
        using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using HttpResponseMessage resp = await _http.PostAsync(_url, content, ct);
        string text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"jev {(int)resp.StatusCode}: {text}");
        return JsonNode.Parse(text)!["answers"]!;
    }

    // Построители вопросов
    public static JsonObject Choice(string instructions, params (string Name, string Text)[] options)
    {
        var criteria = new JsonObject();
        foreach (var o in options) criteria[o.Name] = o.Text;
        return new JsonObject { ["type"] = "choice", ["instructions"] = instructions, ["criteria"] = criteria };
    }

    public static JsonObject YesNo(string instructions) =>
        new JsonObject { ["type"] = "noul", ["instructions"] = instructions };

    public static JsonObject Score(string instructions, params string[] steps) =>
        new JsonObject { ["type"] = "score", ["instructions"] = instructions,
                         ["criteria"] = new JsonArray(steps.Select(s => (JsonNode)s).ToArray()) };
}

// Чтение:
// string pick = answers["action"]!["choice"]!.GetValue<string>();
// double p    = answers["action"]!["probabilities"]![pick]!.GetValue<double>();
// double yes  = answers["urgent"]!["noul"]!.GetValue<double>();
// double frac = answers["level"]!["score"]!.GetValue<double>() / (steps - 1);
```

Рабочий образец в WOM — класс `JevCommander` в `tools/BotArena/Program.cs` (сводка обстановки →
5 вопросов `score` + `choice`, или короткий вариант: `choice` + два `noul`; ответ → веса целей).

## 8. Как встраивать (уроки WOM)

1. **Модель — советник снаружи, а не часть логики.** Детерминированное ядро модель не зовёт. Вызов
   идёт из внешнего слоя, ответ возвращается обычной командой/событием и пишется в журнал. Так тесты,
   реплеи и сохранения не зависят от модели и железа.
2. **Асинхронно и редко.** Ответ — 0,7–2 с. Решение раз в 5–10 с или по событию, не на каждом шаге.
   Пока ответа нет — работать по прошлому решению. Тайм-аут и запасной вариант (правила) обязательны:
   ПК может быть выключен.
3. **За тем же интерфейсом, что и простые правила.** В WOM `ICommander` имеет две реализации:
   правила (`RulesCommander`) и jev. Так их легко сравнивать на стенде один к одному.
4. **Состояние — сжатое и честное.** Только то, что система «знает»; английский; округлённые числа;
   10–40 полей. Каждое лишнее поле — задержка.
5. **Памятка помогает.** В WOM модель повторяла ошибки (штурм при слабом тыле, «копить» всегда).
   Список из 6 коротких правил в `state.commander_handbook` убрал поражения.
6. **Мерить на стенде, а не на глаз.** Журнал «состояние → вопросы → ответ → задержка» в JSONL;
   сравнение с правилами на десятках прогонов.
7. **Модель 9,5 ГБ пользователю не отдать** — это инструмент разработки и опытов, не часть продукта
   (если не решено иначе).

## 9. Итог опыта WOM (для ориентира, не обязателен)

jev командовал игровым ботом (5 целей с весами). 10 партий: jev 3 победы / 0 поражений против 4 / 0 у
простых правил — **на уровне правил, не лучше**; исход решал исполнитель, а не командир. Ценность —
быстрая проверка идей («а что сказала бы модель») и уроки для правил (штурм раньше, когда ракет
много). Подробности — `docs/LLM-COMMANDER.md` в WOM.

## 10. Шпаргалка

```
Адрес:       http://192.168.0.2:11434/v1/systemone      (жив? curl -m 5 …/api/version)
Модель:      nimble
Запрос:      {"model","state":{…},"questions":{имя:{type,instructions,criteria?}}}
Типы:        choice (criteria — объект), noul (без criteria), score (criteria — массив ступеней)
Ответ:       answers.имя.{choice|noul|score, probabilities, confidence, legend}
Скорость:    первый 5–7 с, дальше 0,7–1,4 с; ~1 запрос/с; параллель не помогает
Правила:     вне детерминированной логики, асинхронно, с тайм-аутом и запасным вариантом, журнал
Не отвечает: ПК выключен или брандмауэр (раздел 2)
```
