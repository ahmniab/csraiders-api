namespace CsRaiders.Api.Application.Common.Interfaces.Security;

public interface IOtpGenerator
{
    string Generate();
    byte[] Protect(string email, string otp);
    bool Verify(string email, string otp, byte[] expected);
}
