using Atlas.Domain.Users;
using Atlas.Infrastructure.Security;

namespace Atlas.Infrastructure.Tests.Security;

public class SecurePasswordGeneratorTests
{
    [Fact]
    public void Generated_passwords_satisfy_the_policy_and_are_unique()
    {
        var generator = new SecurePasswordGenerator();
        var seen = new HashSet<string>();

        for (var i = 0; i < 200; i++)
        {
            var password = generator.Generate();
            Assert.Empty(PasswordPolicy.Validate(password));
            Assert.True(seen.Add(password));
        }
    }
}
