// Strings for the route thresholds ("when is the fastest path good enough?"): the resident's explanation and the planner's settings.
// Entries are [en, pl, uk]. Keys of the form th.x.safety / heat / flood / air follow the layer names used by the resident modes.

export const thresholdStrings = {
  // ── Resident: why only the fastest path is shown ──
  'th.fastest.safety': [
    'The fastest path scores {avg} on average and {worst} at its weakest stretch, above the city\'s thresholds for night safety ({tavg} and {tworst}), so no safer alternative is needed.',
    'Najszybsza trasa uzyskuje średnio {avg}, a na najsłabszym odcinku {worst} – powyżej progów miasta dla bezpieczeństwa nocą ({tavg} i {tworst}), więc bezpieczniejsza alternatywa nie jest potrzebna.',
    'Найшвидший шлях має в середньому {avg}, а на найслабшій ділянці {worst} – вище за міські пороги безпеки вночі ({tavg} і {tworst}), тож безпечніша альтернатива не потрібна.'],
  'th.fastest.heat': [
    'The fastest path scores {avg} on average and {worst} at its weakest stretch, above the city\'s thresholds for heat relief ({tavg} and {tworst}), so no cooler alternative is needed.',
    'Najszybsza trasa uzyskuje średnio {avg}, a na najsłabszym odcinku {worst} – powyżej progów miasta dla ulgi cieplnej ({tavg} i {tworst}), więc chłodniejsza alternatywa nie jest potrzebna.',
    'Найшвидший шлях має в середньому {avg}, а на найслабшій ділянці {worst} – вище за міські пороги полегшення від спеки ({tavg} і {tworst}), тож прохолодніша альтернатива не потрібна.'],
  'th.fastest.flood': [
    'The fastest path scores {avg} on average and {worst} at its weakest stretch, above the city\'s thresholds for flood safety ({tavg} and {tworst}), so no drier alternative is needed.',
    'Najszybsza trasa uzyskuje średnio {avg}, a na najsłabszym odcinku {worst} – powyżej progów miasta dla bezpieczeństwa przed powodzią ({tavg} i {tworst}), więc suchsza alternatywa nie jest potrzebna.',
    'Найшвидший шлях має в середньому {avg}, а на найслабшій ділянці {worst} – вище за міські пороги безпеки від повені ({tavg} і {tworst}), тож сухіша альтернатива не потрібна.'],
  'th.fastest.air': [
    'The fastest path scores {avg} on average and {worst} at its weakest stretch, above the city\'s thresholds for air quality ({tavg} and {tworst}), so no cleaner alternative is needed.',
    'Najszybsza trasa uzyskuje średnio {avg}, a na najsłabszym odcinku {worst} – powyżej progów miasta dla jakości powietrza ({tavg} i {tworst}), więc czystsza alternatywa nie jest potrzebna.',
    'Найшвидший шлях має в середньому {avg}, а на найслабшій ділянці {worst} – вище за міські пороги якості повітря ({tavg} і {tworst}), тож чистіша альтернатива не потрібна.'],

  // ── Planner: settings on the weights page ──
  'th.title': ['Fastest-route thresholds', 'Progi dla najszybszej trasy', 'Пороги для найшвидшого маршруту'],
  'th.intro': [
    'How good must the fastest walking route be before residents are shown it without a safer alternative? Below either limit, a safer route is searched for, farther away and with a longer detour allowed. Scores run from 0 to 100; higher is better.',
    'Jak dobra musi być najszybsza trasa piesza, aby mieszkańcy zobaczyli ją bez bezpieczniejszej alternatywy? Poniżej któregokolwiek progu szukana jest bezpieczniejsza trasa, dalej i z dłuższym objazdem. Wyniki są w skali 0–100; wyżej znaczy lepiej.',
    'Наскільки добрим має бути найшвидший пішохідний маршрут, щоб мешканцям показували його без безпечнішої альтернативи? Нижче будь-якого порогу шукається безпечніший маршрут, далі й з довшим об\'їздом. Оцінки від 0 до 100; вище – краще.'],
  'th.average': ['Average score at least', 'Średni wynik co najmniej', 'Середня оцінка щонайменше'],
  'th.worst': ['Weakest stretch at least', 'Najsłabszy odcinek co najmniej', 'Найслабша ділянка щонайменше'],
  'th.defaultIs': ['default {avg} and {worst}', 'domyślnie {avg} i {worst}', 'типово {avg} і {worst}'],
  'th.why': ['Why this default:', 'Dlaczego taka wartość domyślna:', 'Чому таке типове значення:'],
  'th.save': ['Save thresholds', 'Zapisz progi', 'Зберегти пороги'],
  'th.saved': ['Thresholds saved. New routes use them.', 'Progi zapisane. Nowe trasy z nich korzystają.', 'Пороги збережено. Нові маршрути їх використовують.'],
  'th.layerDefaults': ['Use the defaults for this measure', 'Użyj wartości domyślnych dla tego wskaźnika', 'Використати типові значення для цього показника'],
  'th.resetAll': ['Reset thresholds to defaults', 'Przywróć domyślne progi', 'Скинути пороги до типових'],
  'th.resetConfirm': ['Go back to the default thresholds for every measure?', 'Wrócić do domyślnych progów dla wszystkich wskaźników?', 'Повернутися до типових порогів для всіх показників?'],
  'th.resetDone': ['Default thresholds restored.', 'Przywrócono domyślne progi.', 'Типові пороги відновлено.'],
  'th.errRange': ['Enter a whole number from {min} to {max}.', 'Wpisz liczbę od {min} do {max}.', 'Введіть число від {min} до {max}.'],
  'th.errOrder': ['The weakest-stretch limit cannot be higher than the average limit.', 'Próg najsłabszego odcinka nie może być wyższy niż próg średniej.', 'Поріг найслабшої ділянки не може бути вищим за поріг середньої.'],
  'th.why.safety': [
    'At night one dark, empty stretch matters even when the rest is well lit, so the weakest stretch counts as well as the average. 65 on average is a street with working lamps and some open places; below 45 anywhere means a walker is effectively alone in the dark.',
    'Nocą jeden ciemny, pusty odcinek ma znaczenie, nawet gdy reszta jest dobrze oświetlona, dlatego liczy się i najsłabszy odcinek, i średnia. Średnia 65 to ulica z działającymi latarniami i kilkoma otwartymi miejscami; poniżej 45 gdziekolwiek pieszy jest w ciemności praktycznie sam.',
    'Вночі одна темна безлюдна ділянка має значення, навіть коли решта добре освітлена, тому враховують і найслабшу ділянку, і середню. Середня 65 – це вулиця з робочими ліхтарями та кількома відкритими місцями; нижче 45 будь-де пішохід практично сам у темряві.'],
  'th.why.heat': [
    'Heat relief adds up over the whole walk (shade, water, places to sit and cool down), so the average carries the decision. 65 on average means most of the walk is covered; 45 for the worst stretch tolerates one short exposed section, which a walker can cross quickly.',
    'Ulga cieplna sumuje się na całej trasie (cień, woda, miejsca do odpoczynku i ochłody), więc o decyzji przesądza średnia. Średnia 65 oznacza, że większość trasy jest osłonięta; 45 dla najsłabszego odcinka toleruje jeden krótki odsłonięty fragment, który pieszy szybko przejdzie.',
    'Полегшення від спеки складається з усього шляху (тінь, вода, місця для відпочинку й охолодження), тож рішення залежить від середньої. Середня 65 означає, що більша частина шляху прикрита; 45 для найслабшої ділянки припускає один короткий відкритий фрагмент, який пішохід швидко проходить.'],
  'th.why.flood': [
    'Flood risk is local: one low stretch beside a river or an underpass can stop a walk entirely, so the weakest stretch is as important as the average. The default is deliberately not relaxed; raise the weakest-stretch limit if the city wants flood-prone stretches avoided more strictly.',
    'Ryzyko powodzi jest lokalne: jeden niski odcinek przy rzece lub w tunelu może całkowicie przerwać spacer, dlatego najsłabszy odcinek jest tak samo ważny jak średnia. Wartość domyślna celowo nie jest złagodzona; podnieś próg najsłabszego odcinka, jeśli miasto chce ściślej unikać odcinków zagrożonych powodzią.',
    'Ризик повені локальний: одна низька ділянка біля річки чи підземного переходу може зовсім зупинити прогулянку, тому найслабша ділянка так само важлива, як середня. Типове значення навмисно не послаблене; підніміть поріг найслабшої ділянки, якщо місто хоче суворіше уникати небезпечних від повені ділянок.'],
  'th.why.air': [
    'Air quality is mostly felt as exposure over the whole walk, with main roads adding to it, so the average decides. 65 on average keeps a walker mostly away from traffic; 45 for the worst stretch tolerates crossing or walking a short way along a busy road.',
    'Jakość powietrza odczuwa się głównie jako narażenie na całej trasie, a główne drogi je zwiększają, więc decyduje średnia. Średnia 65 trzyma pieszego głównie z dala od ruchu; 45 dla najsłabszego odcinka toleruje przejście przez ruchliwą drogę lub krótki spacer wzdłuż niej.',
    'Якість повітря відчувається переважно як вплив на всьому шляху, а головні дороги його посилюють, тож вирішує середня. Середня 65 тримає пішохода переважно подалі від руху; 45 для найслабшої ділянки припускає перетин жвавої дороги або короткий шлях уздовж неї.']
};
