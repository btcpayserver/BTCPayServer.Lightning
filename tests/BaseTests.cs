using System;
using Xunit.Abstractions;

namespace BTCPayServer.Lightning.Tests;

public class BaseTests
{
    #if DEBUG
        public const int Timeout = 20 * 60 * 1000;
    #else
            public const int Timeout = 2 * 60 * 1000;
    #endif

    static BaseTests()
    {
        Docker = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("IN_DOCKER_CONTAINER"));
    }
    public BaseTests(ITestOutputHelper helper)
    {
        Logs.Tester = new XUnitLog(helper) { Name = "Tests" };
        Logs.LogProvider = new XUnitLogProvider(helper);
        ConnectChannels.Logs = Logs.LogProvider.CreateLogger("Tests");
    }

    public static bool Docker { get; }
}
