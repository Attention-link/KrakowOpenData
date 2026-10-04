// Planner strings for the fifth layer, Accessibility (the tab, its profile switch, "no data", the figures, the weights and thresholds,
// the suggested actions and the alert template). Entries are [en, pl, uk]. Names of the layer, the profiles and the shared wording
// ("mode.access", "accl.*") live in strings-accesslayer.js.

export const plannerAccessStrings = {
  // ── The tab and the pages ──
  'event.access': ['Accessibility', 'Dostępność', 'Доступність'],
  'pm.legend': ['Colour legend', 'Legenda kolorów', 'Легенда кольорів'],
  'pm.legendShow': ['Show the colour legend', 'Pokaż legendę kolorów', 'Показати легенду кольорів'],
  'ov.low.access': ['Very difficult (0)', 'Bardzo trudno (0)', 'Дуже важко (0)'],
  'ov.high.access': ['Easy (100)', 'Łatwo (100)', 'Легко (100)'],
  'ov.distHelp.access': ['Number of squares with data by accessibility score for the chosen profile (higher = easier to get around). Click a bar for what the range means.', 'Liczba kwadratów z danymi wg wskaźnika dostępności dla wybranego profilu (wyżej = łatwiej się poruszać). Kliknij słupek, aby zobaczyć znaczenie przedziału.', 'Кількість квадратів з даними за індексом доступності для вибраного профілю (вище = легше пересуватися). Натисніть стовпчик, щоб побачити значення діапазону.'],
  'rep.showing.access': ['Showing barrier reports only (planning for accessibility).', 'Pokazuję tylko zgłoszenia barier (planowanie dostępności).', 'Показано лише звіти про бар’єри (планування доступності).'],
  'pa.thr.title.wheelchair': ['Accessibility: wheelchair', 'Dostępność: wózek inwalidzki', 'Доступність: інвалідний візок'],
  'pa.thr.title.pram': ['Accessibility: pram', 'Dostępność: wózek dziecięcy', 'Доступність: дитячий візок'],
  'pa.thr.title.mobility': ['Accessibility: limited mobility', 'Dostępność: ograniczona mobilność', 'Доступність: обмежена мобільність'],
  'th.why.access.wheelchair': [
    'For a wheelchair one barrier can stop the walk (a flight of steps, a high kerb, a blocked path), so the weakest stretch counts as much as the average. The default is not relaxed: 65 on average means most of the route is step-free and smooth; below 45 anywhere is a stretch with a barrier that a wheelchair cannot pass.',
    'Dla wózka inwalidzkiego jedna bariera może przerwać spacer (schody, wysoki krawężnik, zablokowana droga), dlatego najsłabszy odcinek liczy się tak samo jak średnia. Wartość domyślna nie jest złagodzona: średnia 65 oznacza, że większość trasy jest bez stopni i równa; poniżej 45 gdziekolwiek to odcinek z barierą nie do pokonania wózkiem.',
    'Для інвалідного візка одна перешкода може зупинити прогулянку (сходи, високий бордюр, заблокований шлях), тому найслабша ділянка так само важлива, як середня. Типове значення не послаблене: середня 65 означає, що більша частина маршруту без сходинок і рівна; нижче 45 будь-де – ділянка з бар’єром, який візок не здолає.'],
  'th.why.access.pram': [
    'A pram copes with some kerbs and with a ramp made for strollers, but a rough surface and steps hurt over the whole walk, so the average carries the decision. 65 on average is a mostly smooth, ramped route; 45 for the worst stretch tolerates one awkward spot that can be crossed by lifting the pram.',
    'Wózek dziecięcy radzi sobie z niektórymi krawężnikami i z rampą dla wózków, ale zła nawierzchnia i schody dokuczają na całej trasie, więc decyduje średnia. Średnia 65 to trasa w większości równa i z rampami; 45 dla najsłabszego odcinka toleruje jedno trudne miejsce, które można pokonać, unosząc wózek.',
    'Дитячий візок долає деякі бордюри та пандус для візків, але погане покриття й сходи дошкуляють на всьому шляху, тож вирішує середня. Середня 65 – це переважно рівний маршрут із пандусами; 45 для найслабшої ділянки припускає одне складне місце, яке можна подолати, підняв візок.'],
  'th.why.access.mobility': [
    'For people who walk with difficulty the length of the walk and the lack of places to rest matter more than a single step, so the average decides. 65 on average means gentle slopes and benches along the way; 45 for the worst stretch tolerates a short steep or rough section.',
    'Dla osób, które chodzą z trudem, długość spaceru i brak miejsc do odpoczynku znaczą więcej niż pojedynczy stopień, więc decyduje średnia. Średnia 65 oznacza łagodne nachylenia i ławki po drodze; 45 dla najsłabszego odcinka toleruje krótki stromy lub nierówny fragment.',
    'Для людей, які ходять із труднощами, довжина шляху й відсутність місць для відпочинку важать більше, ніж одна сходинка, тож вирішує середня. Середня 65 означає плавні ухили та лавки дорогою; 45 для найслабшої ділянки припускає короткий крутий або нерівний відтинок.'],

  // ── Factors (nine per profile) ──
  'pa.factor.steps': ['Steps without a ramp', 'Schody bez podjazdu', 'Сходи без пандуса'],
  'pa.factor.kerbs': ['Raised kerbs', 'Wysokie krawężniki', 'Високі бордюри'],
  'pa.factor.surface': ['Smooth surface', 'Równa nawierzchnia', 'Рівне покриття'],
  'pa.factor.slope': ['Steep slopes', 'Strome nachylenia', 'Круті ухили'],
  'pa.factor.stepFree': ['Step-free entrances', 'Wejścia bez stopni', 'Входи без сходинок'],
  'pa.factor.accessStops': ['Accessible stops', 'Dostępne przystanki', 'Доступні зупинки'],
  'pa.factor.accessToilets': ['Accessible toilets', 'Dostępne toalety', 'Доступні туалети'],
  'pa.factor.rest': ['Places to rest (benches)', 'Miejsca odpoczynku (ławki)', 'Місця відпочинку (лавки)'],
  'pa.factor.tactile': ['Tactile paving', 'Oznaczenia dotykowe', 'Тактильне покриття'],
  'pa.adds': ['{n} of {max} possible accessibility points', '{n} z {max} możliwych pkt dostępności', '{n} з {max} можливих балів доступності'],
  'pa.cellNote': ['For each factor: the closer it is, or the fewer barriers there are, the more of its weight is added. The score is the total, less open barrier reports.', 'Dla każdego czynnika: im jest bliżej albo im mniej barier, tym więcej jego wagi się dodaje. Wynik to suma, pomniejszona o otwarte zgłoszenia barier.', 'Для кожного чинника: чим ближче він або чим менше бар’єрів, тим більше його ваги додається. Оцінка – це сума мінус відкриті звіти про бар’єри.'],

  // ── Figures ──
  'kpi.accessCoverage': ['Squares with accessibility data', 'Kwadraty z danymi o dostępności', 'Квадрати з даними про доступність'],
  'kpi.accessCoverage.help': ['Share of built-up squares that can be scored', 'Odsetek zabudowanych kwadratów, które można ocenić', 'Частка забудованих квадратів, які можна оцінити'],
  'kpi.cellsNoData': ['Squares with no data', 'Kwadraty bez danych', 'Квадрати без даних'],
  'kpi.cellsNoData.help': ['Not scored, not in any average', 'Bez oceny, poza wszystkimi średnimi', 'Без оцінки, поза всіма середніми'],
  'kpi.stepsBarriers': ['Squares with many barriers', 'Kwadraty z wieloma barierami', 'Квадрати з багатьма бар’єрами'],
  'kpi.stepsBarriers.help': ['Steps or kerbs score low (squares with data)', 'Niska ocena schodów lub krawężników (kwadraty z danymi)', 'Низька оцінка сходів або бордюрів (квадрати з даними)'],
  'kpi.noAccessibleStop400': ['No accessible stop within 400 m', 'Brak dostępnego przystanku w promieniu 400 m', 'Немає доступної зупинки в радіусі 400 м'],
  'kpi.noAccessibleStop400.help': ['Squares with data and no accessible stop within 400 m', 'Kwadraty z danymi bez dostępnego przystanku w promieniu 400 m', 'Квадрати з даними без доступної зупинки в радіусі 400 м'],
  'kpi.noAccessibleToilet800': ['No accessible toilet within 800 m', 'Brak dostępnej toalety w promieniu 800 m', 'Немає доступного туалету в радіусі 800 м'],
  'kpi.noAccessibleToilet800.help': ['Squares with data and no accessible toilet within 800 m', 'Kwadraty z danymi bez dostępnej toalety w promieniu 800 m', 'Квадрати з даними без доступного туалету в радіусі 800 м'],
  'kpi.noRest300': ['No place to rest within 300 m', 'Brak miejsca odpoczynku w promieniu 300 m', 'Немає місця для відпочинку в радіусі 300 м'],
  'kpi.noRest300.help': ['Squares with data and no bench within 300 m', 'Kwadraty z danymi bez ławki w promieniu 300 m', 'Квадрати з даними без лавки в радіусі 300 м'],

  // ── No data ──
  'pa.nodata.title': ['Squares with no accessibility data', 'Kwadraty bez danych o dostępności', 'Квадрати без даних про доступність'],
  'pa.nodata.chip': ['{n} squares have no data', 'Kwadraty bez danych: {n}', 'Квадрати без даних: {n}'],
  'pa.nodata.count': ['{n} squares have no data', 'Kwadraty bez danych: {n}', 'Квадрати без даних: {n}'],
  'pa.nodata.short': ['{with} squares are scored. The grey ones are not scored and are left out of every average and ranking; no data is never shown as good or as critical.', 'Ocenione kwadraty: {with}. Szare nie mają oceny i są pominięte w każdej średniej i rankingu; brak danych nigdy nie jest pokazywany jako dobry ani krytyczny.', 'Оцінено квадратів: {with}. Сірі квадрати не мають оцінки й не враховуються в жодному середньому чи рейтингу; відсутність даних ніколи не показується як добре чи критично.'],
  'pa.nodata.explainBtn': ['What does no data mean?', 'Co oznacza brak danych?', 'Що означає відсутність даних?'],
  'pa.nodata.none': ['Accessibility data is not loaded', 'Dane o dostępności nie są wczytane', 'Дані про доступність не завантажено'],
  'pa.nodata.noneHelp': ['There are no accessibility scores yet. Nothing is shown as accessible or inaccessible until the data is loaded.', 'Nie ma jeszcze wyników dostępności. Do czasu wczytania danych nic nie jest pokazywane jako dostępne ani niedostępne.', 'Оцінок доступності ще немає. Доки дані не завантажено, ніщо не показується як доступне чи недоступне.'],
  'pa.nodata.squares': ['squares with no data', 'kwadratów bez danych', 'квадратів без даних'],
  'pa.nodata.what': ['Accessibility is scored only where data was downloaded (OpenStreetMap and ZTP) and something is mapped nearby. Other squares have no score. No data is not the same as accessible, and not the same as inaccessible.', 'Dostępność jest oceniana tylko tam, gdzie pobrano dane (OpenStreetMap i ZTP) i coś jest w pobliżu zmapowane. Pozostałe kwadraty nie mają wyniku. Brak danych to nie to samo co dostępne i nie to samo co niedostępne.', 'Доступність оцінюється лише там, де завантажено дані (OpenStreetMap і ZTP) і поблизу щось нанесено. Інші квадрати оцінки не мають. Відсутність даних – це не те саме, що доступно, і не те саме, що недоступно.'],
  'pa.nodata.withData': ['Squares with data', 'Kwadraty z danymi', 'Квадрати з даними'],
  'pa.nodata.area': ['Downloaded area', 'Pobrany obszar', 'Завантажена зона'],
  'pa.nodata.areaUnknown': ['not known', 'nieznany', 'невідомо'],
  'pa.nodata.reasons': ['Why a square has no data', 'Dlaczego kwadrat nie ma danych', 'Чому в квадрата немає даних'],
  'pa.nodata.reasonsList': ['Outside the downloaded area, or nothing about accessibility is mapped within 250 m.', 'Poza pobranym obszarem albo w promieniu 250 m nie zmapowano nic o dostępności.', 'Поза завантаженою зоною або в радіусі 250 м нічого не нанесено щодо доступності.'],
  'pa.nodata.excluded': ['In averages and rankings', 'W średnich i rankingach', 'У середніх і рейтингах'],
  'pa.nodata.excludedHelp': ['Left out. Every average, share, histogram bar, factor gap and priority list counts only squares with data.', 'Pomijane. Każda średnia, odsetek, słupek histogramu, luka czynnika i lista priorytetów uwzględnia tylko kwadraty z danymi.', 'Не враховуються. Кожне середнє, частка, стовпчик гістограми, прогалина чинника й список пріоритетів враховує лише квадрати з даними.'],
  'pa.nodata.source': ['OpenStreetMap accessibility tags and the ZTP timetable, for the downloaded area.', 'Znaczniki dostępności OpenStreetMap i rozkład jazdy ZTP dla pobranego obszaru.', 'Теги доступності OpenStreetMap і розклад ZTP для завантаженої зони.'],
  'pa.nodata.notInChart': ['{n} squares with no data are not in this chart.', 'Kwadraty bez danych ({n}) nie są uwzględnione na tym wykresie.', 'Квадрати без даних ({n}) не входять до цієї діаграми.'],
  'pa.nodata.cell': ['No accessibility data for this square', 'Brak danych o dostępności dla tego kwadratu', 'Немає даних про доступність для цього квадрата'],
  'pa.nodata.never': ['It has no score. Not scored is not the same as accessible.', 'Nie ma wyniku. Brak oceny to nie to samo co dostępne.', 'Оцінки немає. Відсутність оцінки – це не те саме, що доступно.'],
  'pa.nodata.inScale': ['Squares with no data are not on this scale: they have no score and are shown grey.', 'Kwadraty bez danych nie leżą na tej skali: nie mają wyniku i są pokazane na szaro.', 'Квадрати без даних не входять до цієї шкали: оцінки в них немає, вони сірі.'],
  'pa.map.nodata': ['Grey squares ({n}) have no accessibility data: not scored, and neither accessible nor critical. Click for details.', 'Szare kwadraty ({n}) nie mają danych o dostępności: bez wyniku, ani dostępne, ani krytyczne. Kliknij, aby zobaczyć szczegóły.', 'Сірі квадрати ({n}) не мають даних про доступність: без оцінки, ні доступні, ні критичні. Натисніть, щоб побачити подробиці.'],

  // ── Suggested actions (codes of the API) ──
  'action.ADD_RAMP': ['Add a ramp or a lift next to steps and at stepped entrances', 'Dodać podjazd lub windę przy schodach i przy wejściach ze stopniami', 'Додати пандус або ліфт біля сходів і біля входів зі сходинками'],
  'action.LOWER_KERBS': ['Lower or level kerbs at crossings', 'Obniżyć lub wyrównać krawężniki na przejściach', 'Знизити або вирівняти бордюри на переходах'],
  'action.REPAIR_FOOTWAY': ['Repair rough, narrow or unpaved footways', 'Naprawić zniszczone, wąskie lub nieutwardzone chodniki', 'Відремонтувати зруйновані, вузькі або ґрунтові тротуари'],
  'action.REVIEW_SLOPES': ['Review steep stretches: a gentler alternative, handrails, places to rest', 'Sprawdzić strome odcinki: łagodniejsza alternatywa, poręcze, miejsca odpoczynku', 'Перевірити круті ділянки: м’якша альтернатива, поручні, місця відпочинку'],
  'action.ADD_STEP_FREE_ACCESS': ['Make a public entrance step-free', 'Zapewnić publiczne wejście bez stopni', 'Зробити публічний вхід без сходинок'],
  'action.REVIEW_ACCESSIBLE_STOPS': ['Review stop accessibility with ZTP', 'Sprawdzić dostępność przystanków wspólnie z ZTP', 'Перевірити доступність зупинок разом із ZTP'],
  'action.ADD_ACCESSIBLE_TOILET': ['Provide an accessible public toilet', 'Zapewnić dostępną toaletę publiczną', 'Облаштувати доступний громадський туалет'],
  'action.ADD_BENCHES': ['Add benches along the main walking routes', 'Dodać ławki wzdłuż głównych tras pieszych', 'Додати лавки вздовж головних пішохідних маршрутів'],
  'action.ADD_TACTILE_PAVING': ['Add tactile paving at crossings and stops', 'Dodać oznaczenia dotykowe na przejściach i przystankach', 'Додати тактильне покриття на переходах і зупинках'],

  // ── Brief to an agency ──
  'pa.brief.access': ['Accessibility ({profile}): {score} ({band}).', 'Dostępność ({profile}): {score} ({band}).', 'Доступність ({profile}): {score} ({band}).'],
  'pa.brief.noData': ['No accessibility data for this square: it has no score, which is not the same as accessible.', 'Brak danych o dostępności dla tego kwadratu: nie ma wyniku, co nie oznacza, że jest dostępnie.', 'Для цього квадрата немає даних про доступність: оцінки немає, і це не означає, що доступно.'],

  // ── Alert template ──
  'al.tpl.access': ['Accessibility: blocked or broken access', 'Dostępność: zablokowany lub zepsuty dostęp', 'Доступність: заблокований або несправний доступ'],
  'al.tpl.access.title': ['Step-free access limited', 'Ograniczony dostęp bez stopni', 'Обмежений безбар’єрний доступ'],
  'al.tpl.access.body': ['A path, ramp or lift near you is blocked or out of order. If you use a wheelchair or a pram, or find steps hard, check your route before you set out.', 'Droga, podjazd lub winda w pobliżu jest zablokowana lub nieczynna. Jeśli poruszasz się na wózku inwalidzkim lub z wózkiem dziecięcym albo trudno ci pokonywać schody, sprawdź trasę przed wyjściem.', 'Шлях, пандус або ліфт поблизу заблокований чи не працює. Якщо ви пересуваєтеся на візку чи з дитячим візком або вам важко долати сходи, перевірте маршрут перед виходом.']
};
