using Tahona.Application.Common;

namespace Tahona.Application.Features.Auth;

/// <summary>The plain-text account e-mails, in Arabic and English in one message.</summary>
public static class AccountEmails
{
    public static EmailMessage Invitation(string to, string link, string? clinicName, int trialDays, DateTime expiresAtUtc) => new(to,
        "Your TahonaOS invitation · دعوتك إلى TahonaOS",
        $"""
        مرحباً،

        تمت دعوتك لإنشاء حساب {(clinicName is null ? "عيادتك" : $"«{clinicName}»")} على TahonaOS، مع فترة تجريبية مدتها {trialDays} يوماً.
        افتح الرابط التالي لتسمية العيادة واختيار كلمة المرور:

        {link}

        يعمل الرابط مرة واحدة وتنتهي صلاحيته بتاريخ {expiresAtUtc:yyyy-MM-dd}.

        ----

        Hello,

        You have been invited to create {(clinicName is null ? "your clinic's account" : $"the account for {clinicName}")} on TahonaOS, with a {trialDays}-day trial.
        Open this link to name the clinic and choose a password:

        {link}

        The link works once and expires on {expiresAtUtc:yyyy-MM-dd}.
        If you were not expecting this, you can ignore this e-mail.
        """);

    public static EmailMessage PasswordReset(string to, string link, DateTime expiresAtUtc) => new(to,
        "Reset your TahonaOS password · إعادة تعيين كلمة المرور",
        $"""
        مرحباً،

        طُلبت إعادة تعيين كلمة المرور لحسابك على TahonaOS. افتح الرابط التالي لاختيار كلمة مرور جديدة:

        {link}

        يعمل الرابط مرة واحدة وتنتهي صلاحيته خلال 24 ساعة. إن لم تطلب ذلك فتجاهل هذه الرسالة، ولن يتغير شيء.

        ----

        Hello,

        A password reset was requested for your TahonaOS account. Open this link to choose a new password:

        {link}

        The link works once and expires at {expiresAtUtc:yyyy-MM-dd HH:mm} UTC. If you did not ask for this, ignore this e-mail and nothing changes.
        """);
}
