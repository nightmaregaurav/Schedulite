using Microsoft.Extensions.DependencyInjection;
using Schedulite.DependencyInjection;

namespace Schedulite.UnitTests;

public sealed class RegistrationValidationTests
{
    [Fact]
    public void AddSchedulite_RejectsDuplicateDefaultAndNamedRuntimes()
    {
        var services = new ServiceCollection();

        services.AddSchedulite<TestScheduleProvider>();
        Assert.Throws<ArgumentException>(() => services.AddSchedulite<TestScheduleProvider>());

        services.AddSchedulite<TestScheduleProvider>("billing");
        Assert.Throws<ArgumentException>(() => services.AddSchedulite<TestScheduleProvider>("billing"));
    }
}
