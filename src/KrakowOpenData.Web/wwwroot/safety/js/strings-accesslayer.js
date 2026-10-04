// Strings for the fifth layer, Accessibility ("Dostępność"): the resident tab, its legend (with "No data"), place card, profile switch and the
// "Find accessible path" walk. Entries are [en, pl, uk]; a missing translation falls back to English.
// Keys follow the other layers (mode.access, home.walk.access, route.avg.access ...). The older barrier browser keeps its own acc.* keys
// (strings-access.js). Planner texts (ov.*, al.tpl.*, th.why.*, kpi.*) belong to the planner string files.

const base = {
  // ── The tab ──
  'mode.access': ['Accessibility', 'Dostępność', 'Доступність'],
  'mode.access.score': ['Accessibility score', 'Wskaźnik dostępności', 'Індекс доступності'],
  'explain.dir.access': ['Higher = easier to get around', 'Wyżej = łatwiej się poruszać', 'Вище = легше пересуватися'],
  'legend.accessTitle': ['Accessibility score (0 very difficult – 100 easy)', 'Wskaźnik dostępności (0 bardzo trudno – 100 łatwo)', 'Індекс доступності (0 дуже важко – 100 легко)'],
  'cov.access': ['Counts steps, kerbs and rough or steep stretches within about 150 m; looks for step-free entrances, accessible stops, toilets and benches within {r} (dashed circle on the map).',
    'Liczymy schody, krawężniki oraz nierówne lub strome odcinki w promieniu ok. 150 m; szukamy wejść bez schodów, dostępnych przystanków, toalet i ławek w promieniu {r} (przerywane koło na mapie).',
    'Рахуємо сходи, бордюри та нерівні або круті ділянки в радіусі близько 150 м; шукаємо входи без сходів, доступні зупинки, туалети й лавки в радіусі {r} (пунктирне коло на мапі).'],
  'how.access': ['Mapped barriers (steps without a ramp, high kerbs, rough or steep footways) and amenities (step-free entrances and lifts, accessible stops and toilets, benches, tactile paving), weighed for the profile you choose: wheelchair, pram or limited mobility. Where nothing is mapped there is no score: no data is not the same as accessible.',
    'Zmapowane bariery (schody bez rampy, wysokie krawężniki, nierówne lub strome chodniki) i udogodnienia (wejścia bez schodów i windy, dostępne przystanki i toalety, ławki, płytki dotykowe), ważone dla wybranego profilu: wózek inwalidzki, wózek dziecięcy lub ograniczona mobilność. Tam, gdzie nic nie zmapowano, nie ma wyniku: brak danych nie oznacza dostępności.',
    'Нанесені на мапу бар’єри (сходи без пандуса, високі бордюри, нерівні або круті тротуари) та зручності (входи без сходів і ліфти, доступні зупинки й туалети, лавки, тактильна плитка) з вагами для обраного профілю: інвалідний візок, дитячий візок або обмежена мобільність. Де нічого не нанесено, оцінки немає: брак даних не означає доступності.'],
  'home.suggest.access': ['Accessibility is the most useful view for you right now.', 'Dostępność jest teraz dla Ciebie najbardziej przydatnym widokiem.', 'Доступність зараз найкорисніший для вас погляд.'],

  // ── Profile switch ──
  'accl.profile.title': ['Whose way is it?', 'Dla kogo trasa?', 'Для кого маршрут?'],
  'accl.profile.label': ['Accessibility profile', 'Profil dostępności', 'Профіль доступності'],
  'accl.p.wheelchair': ['Wheelchair', 'Wózek inwalidzki', 'Інвалідний візок'],
  'accl.p.pram': ['Pram', 'Wózek dziecięcy', 'Дитячий візок'],
  'accl.p.mobility': ['Limited mobility', 'Ograniczona mobilność', 'Обмежена мобільність'],
  'accl.emph.wheelchair': ['Counts steps and kerbs most: they stop a wheelchair completely.', 'Najbardziej liczą się schody i krawężniki: całkiem zatrzymują wózek inwalidzki.', 'Найбільше важать сходи й бордюри: вони повністю зупиняють візок.'],
  'accl.emph.pram': ['Counts the surface and kerbs most; a ramp made for prams is enough.', 'Najbardziej liczą się nawierzchnia i krawężniki; wystarczy rampa dla wózków dziecięcych.', 'Найбільше важать покриття й бордюри; достатньо пандуса для візків.'],
  'accl.emph.mobility': ['Counts places to rest and slope most; steps are hard rather than impossible.', 'Najbardziej liczą się miejsca do odpoczynku i nachylenie; schody są trudne, ale nie niemożliwe.', 'Найбільше важать місця для відпочинку й ухил; сходи складні, але не нездоланні.'],
  'accl.privacy': ['Only a preference about barriers, kept on this device. We never ask about health.', 'To tylko preferencja dotycząca barier, zapisana na tym urządzeniu. Nie pytamy o zdrowie.', 'Це лише вибір щодо бар’єрів, збережений на цьому пристрої. Ми не питаємо про здоров’я.'],
  'accl.browse': ['Browse barriers and amenities', 'Przeglądaj bariery i udogodnienia', 'Переглянути бар’єри та зручності'],
  'accl.browseHelp': ['The full list of steps, kerbs, lifts, toilets and benches around a place, with sources and corrections.', 'Pełna lista schodów, krawężników, wind, toalet i ławek w okolicy, ze źródłami i możliwością poprawek.', 'Повний список сходів, бордюрів, ліфтів, туалетів і лавок поблизу, з джерелами та виправленнями.'],

  // ── No data (never read as accessible) ──
  'legend.noData': ['No data', 'Brak danych', 'Немає даних'],
  'legend.noData.desc': ['Nothing mapped here. Not the same as accessible, and never counted as good or critical.', 'Nic tu nie zmapowano. To nie to samo co dostępne i nigdy nie liczy się jako dobre ani krytyczne.', 'Тут нічого не нанесено. Це не те саме, що доступно, і ніколи не вважається ні добрим, ні критичним.'],
  'accl.factorNoData': ['No data: no stop in the ZTP open data is flagged wheelchair-accessible, so this factor is left out of the score.', 'Brak danych: żaden przystanek w otwartych danych ZTP nie jest oznaczony jako dostępny dla wózków, więc ten czynnik nie wlicza się do wyniku.', 'Немає даних: жодну зупинку у відкритих даних ZTP не позначено як доступну для візків, тому цей чинник не враховано в оцінці.'],
  'accl.noData': ['No data', 'Brak danych', 'Немає даних'],
  'accl.noDataHere': ['No accessibility data here', 'Brak danych o dostępności w tym miejscu', 'Немає даних про доступність у цьому місці'],
  'accl.noDataTip': ['No accessibility data here (not the same as accessible)', 'Brak danych o dostępności (to nie znaczy, że jest dostępnie)', 'Немає даних про доступність (це не означає, що доступно)'],
  'accl.noData.outside_area': ['This place is outside the area with accessibility data. There is no score here, and no data is not the same as accessible.', 'To miejsce leży poza obszarem z danymi o dostępności. Nie ma tu wyniku, a brak danych nie oznacza dostępności.', 'Це місце поза зоною з даними про доступність. Тут немає оцінки, а брак даних не означає доступності.'],
  'accl.noData.no_data': ['Nothing is mapped about accessibility around here. There is no score; it does not mean there are no barriers.', 'W okolicy nic nie zmapowano w sprawie dostępności. Nie ma wyniku; nie znaczy to, że nie ma barier.', 'Поблизу нічого не нанесено щодо доступності. Оцінки немає; це не означає, що бар’єрів немає.'],
  'accl.noData.unavailable': ['Accessibility data is not loaded yet, so there is no score.', 'Dane o dostępności nie zostały jeszcze wczytane, więc nie ma wyniku.', 'Дані про доступність ще не завантажено, тому оцінки немає.'],
  'accl.noData.thin_coverage': ['Few items are mapped nearby, so this score is less certain. What is not mapped is not counted as accessible.', 'W pobliżu zmapowano niewiele obiektów, więc ten wynik jest mniej pewny. To, czego nie zmapowano, nie liczy się jako dostępne.', 'Поблизу нанесено мало об’єктів, тож оцінка менш певна. Те, що не нанесено, не вважається доступним.'],
  'accl.noData.generic': ['There is no accessibility score for this place.', 'Dla tego miejsca nie ma wyniku dostępności.', 'Для цього місця немає оцінки доступності.'],
  'accl.areaNote': ['Mapped data covers central Kraków. Elsewhere there is no data, and no data is not the same as accessible.', 'Dane obejmują centrum Krakowa. Gdzie indziej nie ma danych, a brak danych nie oznacza dostępności.', 'Дані охоплюють центр Кракова. Деінде даних немає, а брак даних не означає доступності.'],
  'accl.coverage': ['{n} of {total} squares have accessibility data ({p}%).', '{n} z {total} kwadratów ma dane o dostępności ({p}%).', '{n} із {total} квадратів мають дані про доступність ({p}%).'],
  'accl.notLoaded': ['Accessibility data is not loaded yet, so there are no accessibility scores.', 'Dane o dostępności nie zostały jeszcze wczytane, więc nie ma wyników dostępności.', 'Дані про доступність ще не завантажено, тому оцінок доступності немає.'],
  'accl.chip.data': ['Data: {p}% of squares', 'Dane: {p}% kwadratów', 'Дані: {p}% квадратів'],
  'accl.chip.none': ['No accessibility data', 'Brak danych o dostępności', 'Немає даних про доступність'],
  'accl.condTitle': ['Accessibility data', 'Dane o dostępności', 'Дані про доступність'],
  'accl.condProfile': ['Profile', 'Profil', 'Профіль'],
  'accl.condCoverage': ['Coverage', 'Zasięg danych', 'Охоплення даних'],
  'accl.areaOutline': ['Area with accessibility data', 'Obszar z danymi o dostępności', 'Зона з даними про доступність'],

  // ── Place card ──
  'place.walkTo.access': ['Find accessible path here', 'Znajdź dostępną trasę tutaj', 'Знайти доступний маршрут сюди'],
  'place.near.access': ['Step-free access, stops, toilets and rest nearby', 'Wejścia bez schodów, przystanki, toalety i miejsca odpoczynku w pobliżu', 'Входи без сходів, зупинки, туалети й місця відпочинку поблизу'],
  'place.accessNote': ['The score comes from mapped barriers and amenities (OpenStreetMap, ZTP stop data), not from a survey of the street. A barrier nobody mapped is not counted.', 'Wynik pochodzi ze zmapowanych barier i udogodnień (OpenStreetMap, dane ZTP o przystankach), a nie z badania ulicy. Bariera, której nikt nie zmapował, nie jest liczona.', 'Оцінка ґрунтується на нанесених бар’єрах і зручностях (OpenStreetMap, дані ZTP про зупинки), а не на обстеженні вулиці. Бар’єр, якого ніхто не нанес, не враховується.'],
  'accl.otherProfiles': ['The same place for the other profiles', 'To samo miejsce dla innych profili', 'Те саме місце для інших профілів'],
  'accl.scoreOf': ['{p}: {n} out of 100', '{p}: {n} na 100', '{p}: {n} зі 100'],
  'accl.scoreNone': ['{p}: no data', '{p}: brak danych', '{p}: немає даних'],
  'accl.reportBarrier': ['Report a barrier', 'Zgłoś barierę', 'Повідомити про бар’єр'],
  'accl.speakNoData': ['{label}: no accessibility data here.', '{label}: brak danych o dostępności w tym miejscu.', '{label}: немає даних про доступність у цьому місці.'],
  'explain.adds.access': ['{n} of {max} possible accessibility points', '{n} z {max} możliwych pkt dostępności', '{n} з {max} можливих балів доступності'],
  'explain.col.access': ['Accessibility points', 'Pkt dostępności', 'Бали доступності'],
  'explain.tableNote.access': ['Each factor adds its share of 100 points. Open barrier reports take points off. A place with no data has no score at all.', 'Każdy czynnik dodaje swoją część ze 100 punktów. Otwarte zgłoszenia barier odejmują punkty. Miejsce bez danych nie ma żadnego wyniku.', 'Кожен чинник додає свою частку зі 100 балів. Відкриті повідомлення про бар’єри віднімають бали. Місце без даних не має жодної оцінки.'],
  'factor.barriers': ['about {n} within 150 m', 'ok. {n} w promieniu 150 m', 'близько {n} у радіусі 150 м'],
  'factor.pctSmooth': ['{n}% smooth', '{n}% gładkich', '{n}% рівних'],
  'factor.notMapped': ['not mapped', 'niezmapowane', 'не нанесено'],
  'kind.Elevator': ['Lift', 'Winda', 'Ліфт'],
  'kind.Entrance': ['Step-free entrance', 'Wejście bez schodów', 'Вхід без сходів'],
  'kind.Place': ['Accessible place', 'Dostępne miejsce', 'Доступне місце'],
  'kind.Bench': ['Bench', 'Ławka', 'Лавка'],
  'kind.TactilePaving': ['Tactile paving', 'Płytki dotykowe', 'Тактильна плитка'],
  'band.access.Good.desc': ['Few barriers: smooth, step-free surroundings with accessible stops, toilets and places to rest.', 'Mało barier: gładkie otoczenie bez schodów, z dostępnymi przystankami, toaletami i miejscami do odpoczynku.', 'Мало бар’єрів: рівне оточення без сходів, з доступними зупинками, туалетами й місцями для відпочинку.'],
  'band.access.Fair.desc': ['Mostly passable: some barriers or a missing amenity.', 'Przeważnie przejezdne: kilka barier lub brak jakiegoś udogodnienia.', 'Здебільшого прохідно: є кілька бар’єрів або бракує зручності.'],
  'band.access.Weak.desc': ['Difficult: several barriers, or important amenities are far.', 'Trudno: kilka barier albo ważne udogodnienia są daleko.', 'Важко: кілька бар’єрів або важливі зручності далеко.'],
  'band.access.Critical.desc': ['Very difficult: barriers all around, little step-free access nearby.', 'Bardzo trudno: bariery dookoła, mało dostępu bez schodów w pobliżu.', 'Дуже важко: бар’єри навколо, мало входів без сходів поблизу.'],

  // ── The "Find accessible path" walk ──
  'home.walk.access': ['Find accessible path', 'Znajdź dostępną trasę', 'Знайти доступний маршрут'],
  'home.walkHelp.access': ['Finds the most accessible way between two places for your profile and says where nothing is known.', 'Znajduje najbardziej dostępną drogę między dwoma miejscami dla Twojego profilu i pokazuje, gdzie brakuje danych.', 'Знаходить найдоступніший шлях між двома місцями для вашого профілю й показує, де немає даних.'],
  'walk.title.access': ['Accessible path', 'Dostępna trasa', 'Доступний маршрут'],
  'walk.intro.access': ['Choose a start and a destination. We look for the way with the fewest barriers for the profile you chose, and say where nothing is mapped.', 'Wybierz początek i cel. Szukamy drogi z najmniejszą liczbą barier dla wybranego profilu i mówimy, gdzie nic nie zmapowano.', 'Оберіть початок і ціль. Ми шукаємо шлях з найменшою кількістю бар’єрів для обраного профілю й повідомляємо, де нічого не нанесено.'],
  'walk.for.access': ['For: {p}', 'Dla: {p}', 'Для: {p}'],
  'walk.changeProfile': ['Change profile', 'Zmień profil', 'Змінити профіль'],
  'walk.weakest.access': ['Hardest spot', 'Najtrudniejsze miejsce', 'Найважче місце'],
  'walk.weakAt.access': ['Help near the hardest spot', 'Pomoc przy najtrudniejszym miejscu', 'Допомога біля найважчого місця'],
  'walk.advice.access.good': ['The whole way scores well for access. Normal care applies.', 'Cała trasa ma dobry wynik dostępności. Zachowaj zwykłą ostrożność.', 'Увесь шлях має добру оцінку доступності. Дотримуйтеся звичайної обережності.'],
  'walk.advice.access.fair': ['Mostly passable, with a harder stretch. Look at it before you set out.', 'Przeważnie przejezdna, z trudniejszym odcinkiem. Obejrzyj go, zanim wyruszysz.', 'Здебільшого прохідно, є важчий відрізок. Подивіться на нього перед виходом.'],
  'walk.advice.access.weak': ['Part of this way is hard to get through. Check it on site or choose another way.', 'Część tej trasy jest trudna do pokonania. Sprawdź ją na miejscu lub wybierz inną drogę.', 'Частину цього шляху важко пройти. Перевірте на місці або оберіть інший шлях.'],
  'route.mostAccessible': ['Most accessible', 'Najbardziej dostępna', 'Найдоступніший'],
  'route.avg.access': ['Average accessibility {n}', 'Średnia dostępność {n}', 'Середня доступність {n}'],
  'route.worst.access': ['Hardest stretch {n}', 'Najtrudniejszy odcinek {n}', 'Найважча ділянка {n}'],
  'route.better.access': ['This route is {g} points more accessible on average than the fastest.', 'Ta trasa jest średnio o {g} pkt bardziej dostępna niż najszybsza.', 'Цей маршрут у середньому на {g} балів доступніший за найшвидший.'],
  'route.none.access': ['No nearby street route is clearly more accessible, so the fastest route is also the best option.', 'Żadna pobliska trasa uliczna nie jest wyraźnie bardziej dostępna, więc najszybsza trasa jest też najlepsza.', 'Жоден сусідній вуличний маршрут не є помітно доступнішим, тож найшвидший маршрут також найкращий.'],
  'route.noData.all': ['There is no accessibility data along this path, which is not the same as accessible. Check the way on site.', 'Brak danych o dostępności na tej trasie, a to nie to samo co dostępna. Sprawdź drogę na miejscu.', 'Немає даних про доступність уздовж цього маршруту, а це не те саме, що доступний. Перевірте шлях на місці.'],
  'route.noData.share': ['{p}% of this path has no accessibility data. It is not counted in the scores above, and it is not the same as accessible.', '{p}% tej trasy nie ma danych o dostępności. Nie wlicza się do wyników powyżej i to nie to samo co dostępna.', '{p}% цього маршруту не має даних про доступність. Це не враховано в оцінках вище й не означає доступності.'],
  'route.noData.chip': ['{p}% no data', '{p}% bez danych', '{p}% без даних'],
  'route.noData.stats': ['No accessibility score along this path', 'Brak wyniku dostępności na tej trasie', 'Немає оцінки доступності на цьому маршруті'],
  'route.fastestUnknown': ['Most of the fastest route is unmapped for accessibility, so we cannot say it is acceptable.', 'Większość najszybszej trasy nie ma danych o dostępności, więc nie możemy powiedzieć, że jest odpowiednia.', 'Більша частина найшвидшого маршруту не має даних про доступність, тож ми не можемо сказати, що він прийнятний.'],
  'th.fastest.access': ['The fastest path scores {avg} on average and {worst} at its hardest stretch, above the city\'s thresholds for accessibility ({tavg} and {tworst}), so no more accessible alternative is needed.',
    'Najszybsza trasa uzyskuje średnio {avg}, a na najtrudniejszym odcinku {worst} – powyżej progów miasta dla dostępności ({tavg} i {tworst}), więc bardziej dostępna alternatywa nie jest potrzebna.',
    'Найшвидший шлях має в середньому {avg}, а на найважчій ділянці {worst} – вище за міські пороги доступності ({tavg} і {tworst}), тож доступніша альтернатива не потрібна.'],

  // ── Reports ──
  'report.intro.access': ['What barrier did you notice: a blocked path, steps with no ramp, a broken lift? Reports from several people count most.', 'Jaka bariera utrudnia przejście: zablokowana droga, schody bez rampy, zepsuta winda? Zgłoszenia od kilku osób liczą się najbardziej.', 'Який бар’єр ви помітили: заблокований шлях, сходи без пандуса, зламаний ліфт? Повідомлення від кількох людей мають найбільшу вагу.']
};

// Factor labels, one set for the three profiles: factor.access.<profile>.<base>
const FACTORS = {
  steps: ['Steps without a ramp', 'Schody bez rampy', 'Сходи без пандуса'],
  kerbs: ['High kerbs', 'Wysokie krawężniki', 'Високі бордюри'],
  surface: ['Smooth footways', 'Gładka nawierzchnia chodników', 'Рівне покриття тротуарів'],
  slope: ['Steep or narrow stretches', 'Strome lub wąskie odcinki', 'Круті або вузькі ділянки'],
  stepFree: ['Step-free entrance or lift', 'Wejście bez schodów lub winda', 'Вхід без сходів або ліфт'],
  accessStops: ['Accessible public transport stop', 'Dostępny przystanek komunikacji', 'Доступна зупинка транспорту'],
  accessToilets: ['Accessible toilet', 'Toaleta dostępna dla wózka', 'Доступний туалет'],
  rest: ['Bench to rest', 'Ławka do odpoczynku', 'Лавка для відпочинку'],
  tactile: ['Tactile paving', 'Płytki dotykowe', 'Тактильна плитка']
};

const factors = {};
for (const p of ['wheelchair', 'pram', 'mobility']) {
  for (const [b, v] of Object.entries(FACTORS)) factors[`factor.access.${p}.${b}`] = v;
}

export const accessLayerStrings = { ...base, ...factors };
