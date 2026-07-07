#if MACCATALYST && DEBUG
using Perezosoft.Shared.Ui.Auth;

namespace Perezosoft.Maui.Auth;

/// <summary>
/// Mac Catalyst Debug-only <see cref="ISessionStore"/>. Local Debug builds are ad-hoc
/// signed, and MAUI SecureStorage uses the data-protection keychain, which needs the
/// restricted keychain-access-groups entitlement — claiming it without a provisioning
/// profile gets the app SIGKILLed at launch, omitting it fails every save with
/// MissingEntitlement. Until dev signing exists (NATIVE-9), the refresh token lives in
/// a user-only (0600) file under Application Support — the same at-rest exposure as the
/// repo's dev .env. Signed Release/store builds keep <see cref="SecureStorageSessionStore"/>.
/// </summary>
public sealed class DebugFileSessionStore : ISessionStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "perezosoft-debug-session");

    public bool UsesBodyTransport => true;

    public Task<string?> GetRefreshTokenAsync()
    {
        try
        {
            return Task.FromResult<string?>(File.Exists(FilePath) ? File.ReadAllText(FilePath) : null);
        }
        catch
        {
            // An unreadable store reads as "no session" rather than crashing (parity with the secure store).
            return Task.FromResult<string?>(null);
        }
    }

    public Task SaveRefreshTokenAsync(string refreshToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, refreshToken);
        File.SetUnixFileMode(FilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        return Task.CompletedTask;
    }

    public Task ClearAsync()
    {
        try { File.Delete(FilePath); } catch { /* already gone */ }
        return Task.CompletedTask;
    }
}
#endif
