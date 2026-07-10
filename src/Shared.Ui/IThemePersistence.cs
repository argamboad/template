namespace Perezosoft.Shared.Ui;

/// <summary>
/// Persists the user's chosen UI theme so the next cold start can re-apply it before first
/// paint. Unlike <see cref="ICulturePersistence"/> no native (C#-side) store is needed: the
/// theme is pure DOM, and <c>theme.js</c> reads <c>localStorage["app_theme"]</c> inside the
/// (web)view at page load on every host. Callers still apply the in-process theme themselves
/// via <c>appTheme.set</c>; this is only the durable half.
/// </summary>
public interface IThemePersistence
{
    Task PersistAsync(string theme);
}
