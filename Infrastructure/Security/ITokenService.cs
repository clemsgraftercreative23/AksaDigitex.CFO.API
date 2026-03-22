using MyBackend.Infrastructure.Persistence.Entities;

namespace MyBackend.Infrastructure.Security;

public interface ITokenService
{
    string CreateAccessToken(UserEntity user, string roleName);
}
