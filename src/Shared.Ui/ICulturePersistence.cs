namespace Perezosoft.Shared.Ui;

/// <summary>
/// Persists the user's chosen UI culture so the host can re-apply it on the next cold start.
/// Each host owns a store its startup path can actually read: web writes
/// <c>localStorage["app_culture"]</c> (read pre-render by <c>Web/Program.cs</c>); MAUI writes
/// OS <c>Preferences</c> (read in <c>MauiProgram</c> — WebView localStorage doesn't exist yet
/// when the native process boots). Callers still set the in-process culture themselves; this
/// is only the durable half.
/// </summary>
public interface ICulturePersistence
{
    Task PersistAsync(string cultureCode);
}
