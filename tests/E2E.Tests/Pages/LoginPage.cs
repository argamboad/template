using Microsoft.Playwright;

namespace Template.E2E.Tests.Pages;

/// <summary>Page object for the login screen (selectors via stable data-testid hooks).</summary>
public class LoginPage(IPage page) : BasePage(page)
{
    public override string Path => "/login";

    public ILocator Email => Page.GetByTestId("login-email");
    public ILocator SendOtp => Page.GetByTestId("login-send-otp");
    public ILocator OtpCode => Page.GetByTestId("login-otp-code");
    public ILocator VerifyOtp => Page.GetByTestId("login-verify-otp");
    public ILocator MfaCode => Page.GetByTestId("login-mfa-code");
    public ILocator VerifyMfa => Page.GetByTestId("login-verify-mfa");

    /// <summary>Requests an OTP, reads it from Mailpit, enters it, and submits.</summary>
    public async Task SignInWithOtpAsync(string email)
    {
        await Email.FillAsync(email);
        await SendOtp.ClickAsync();

        var code = await Mailpit.WaitForOtpAsync(email, TimeSpan.FromSeconds(20));
        await OtpCode.FillAsync(code);
        await VerifyOtp.ClickAsync();
    }

    /// <summary>
    /// OTP sign-in for a user with MFA enabled: after the OTP step the app presents the step-up
    /// challenge; compute a fresh TOTP from <paramref name="totpSecret"/>, enter it, and submit.
    /// </summary>
    public async Task SignInWithOtpAndMfaAsync(string email, string totpSecret)
    {
        await SignInWithOtpAsync(email);
        await Assertions.Expect(MfaCode).ToBeVisibleAsync(new() { Timeout = 30_000 }); // step-up prompt
        await MfaCode.FillAsync(Totp.Now(totpSecret));
        await VerifyMfa.ClickAsync();
    }
}
