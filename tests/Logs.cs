using System;
using System.Text;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace BTCPayServer.Lightning.Tests
{
    public interface ILog
    {
        void LogInformation(string msg);
    }

    public class XUnitLogProvider : ILoggerProvider
    {
        readonly ITestOutputHelper _helper;
        public XUnitLogProvider(ITestOutputHelper helper)
        {
            _helper = helper;
        }
        public ILogger CreateLogger(string categoryName)
        {
            return new XUnitLog(_helper) { Name = categoryName };
        }

        public void Dispose()
        {

        }
    }
    public class XUnitLog : ILog, ILogger, IDisposable
    {
        ITestOutputHelper _Helper;
        public XUnitLog(ITestOutputHelper helper)
        {
            _Helper = helper;
        }

        public string Name { get; set; }

        public IDisposable BeginScope<TState>(TState state) => this;

        public void Dispose()
        {
        }

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var builder = new StringBuilder();
            builder.Append(formatter(state, exception));
            if (exception != null)
            {
                builder.AppendLine();
                builder.Append(exception);
            }
            LogInformation(builder.ToString());
        }

        public void LogInformation(string msg)
        {
            if (msg == null)
                return;
            try
            {
                _Helper.WriteLine(DateTimeOffset.UtcNow + " :" + Name + ":   " + msg);
            }
            catch
            {
                // ignored
            }
        }
    }
    public static class Logs
    {
        public static ILog Tester { get; set; }
        public static XUnitLogProvider LogProvider { get; set; }
    }
}
