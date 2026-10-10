using Atlas.Domain.Users;

namespace Atlas.Application.Common.Abstractions;

public interface ITokenService
{
    AccessToken Create(User user);
}
