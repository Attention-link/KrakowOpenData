namespace KrakowOpenData.Web.Localization;

/// <summary>
/// The language chosen in this browser session (one instance per Blazor circuit).
/// MainLayout listens to <see cref="Changed"/> and re-cascades the new translator, so every page re-renders.
/// </summary>
public sealed class LanguageState
{
    public Translator Current { get; private set; } = Translator.For(AppLanguages.Default);

    public event Action? Changed;

    public void Set(AppLanguage language)
    {
        if (language == Current.Language) return;
        Current = Translator.For(language);
        Changed?.Invoke();
    }
}
