using System.Reflection;
using System.Runtime.Versioning;

namespace BingoBongo.Tests;

public class TargetFrameworkTests
{
    [Fact]
    public void Api_assembly_targets_dotnet_10()
    {
        var attr = typeof(Program).Assembly.GetCustomAttribute<TargetFrameworkAttribute>();

        Assert.Equal(".NETCoreApp,Version=v10.0", attr?.FrameworkName);
    }
}
