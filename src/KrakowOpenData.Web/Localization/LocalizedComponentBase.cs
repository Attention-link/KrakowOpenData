using Microsoft.AspNetCore.Components;

namespace KrakowOpenData.Web.Localization;

/// <summary>
/// Base class for every component (set in Components/_Imports.razor). Exposes the current
/// translator as <c>T</c>: write <c>@T["nav.weather"]</c> in markup.
/// </summary>
public abstract class LocalizedComponentBase : ComponentBase
{
    [CascadingParameter]
    public Translator T { get; set; } = Translator.English;
}
