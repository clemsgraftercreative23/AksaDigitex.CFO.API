namespace MyBackend.Infrastructure.Security;

public interface IPasswordHasherService
{
    string HashPassword(string password);
    bool Verify(string password, string passwordHash);
}
