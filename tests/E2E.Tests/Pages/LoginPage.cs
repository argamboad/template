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

    /// <summary>Requests an OTP, reads it from Mailpit, enters it, and submits.</summary>
    public async Task SignInWithOtpAsync(string email)
    {
        await Email.FillAsync(email);
        await SendOtp.ClickAsync();

        var code = await Mailpit.WaitForOtpAsync(email, TimeSpan.FromSeconds(20));
        await OtpCode.FillAsync(code);
        await VerifyOtp.ClickAsync();
    }
}
