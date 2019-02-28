using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Lightning.TestFramework
{
    public class CLIResult
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
    }

    public class CLIException : Exception
    {
        public int ExitCode { get; set; }
        public string Output { get; set; }
        public override string Message => $"Exit code {ExitCode}: {Output}";
    }
    public abstract class CLIProxyBase
    {
        public string WorkingDirectory { get; set; }
        public ILogger Logger { get; set; }
        public CLIResult Run(string cmd)
            => Run(cmd, true);

        public abstract CLIResult Run(string cdm, bool ignoreError);
        public CLIProxyBase(ILogger logger)
        {
            Logger = logger ?? new LoggerFactory().CreateLogger(nameof(CLIProxyBase));
        }
        public CLIProxyBase()
        {
            Logger = new LoggerFactory().CreateLogger(nameof(CLIProxyBase));
        }
        protected CLIResult Run(ProcessStartInfo pInfo)
        {
            pInfo.WorkingDirectory = WorkingDirectory;
            pInfo.RedirectStandardOutput = true;
            pInfo.UseShellExecute = false;
            pInfo.CreateNoWindow = true;
            var p = new Process()
            {
                StartInfo = pInfo
            };
            Logger.LogInformation($"// Running {pInfo.FileName} {pInfo.Arguments}");
            StringBuilder sb = new StringBuilder();
            p.OutputDataReceived += (s, r) =>
            {
                Logger.LogDebug($"// " + r?.Data);
                sb.AppendLine(r?.Data ?? string.Empty);
            };
            p.ErrorDataReceived += (s, r) =>
            {
                Logger.LogError("// " + r?.Data);
                sb.AppendLine(r?.Data ?? string.Empty);
            };
            p.Start();
            p.BeginOutputReadLine();
            p.WaitForExit(30000);
            if (!p.HasExited)
                p.Kill();
            return new CLIResult() { ExitCode = p.ExitCode, Output = sb.ToString() };
        }

        public void AssertRun(string cmd)
        {
            var result = Run(cmd, false);
            if (result.ExitCode != 0)
            {
                throw new CLIException() { ExitCode = result.ExitCode, Output = result.Output };
            }
        }
    }

    public class CLIFactory
    {
        public static CLIProxyBase CreateShell(ILogger logger = null)
            => (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                ? new PowerShellCLIProxy(logger) as CLIProxyBase : new BashCLIProxy(logger) as CLIProxyBase; 
    }

    public class BashCLIProxy : CLIProxyBase
    {
        public BashCLIProxy(ILogger logger) : base(logger)
        {
        }
        public static string EscapeBash(string cmd) => cmd.Replace("\'", "\\\"");
        public override CLIResult Run(string cmd, bool ignoreError)
        {
            if (ignoreError)
                cmd += "|| true";
            var escapedArgs = EscapeBash(cmd);
            return this.Run(new ProcessStartInfo()
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{escapedArgs}\""
        });
        }
    }
    public class PowerShellCLIProxy : CLIProxyBase
    {
        public PowerShellCLIProxy(ILogger logger) : base(logger)
        {
        }
        public static string EscapePowerShell(string cmd) => cmd.Replace("\"", "\\\"");
        public override CLIResult Run(string cmd, bool ignoreError)
        {
            var escapedArgs = EscapePowerShell(cmd);
            if (ignoreError)
                escapedArgs += "; $LastExitCode = 0";

            return this.Run(new ProcessStartInfo()
            {
                FileName = "powershell.exe",
                Arguments = $"-Command \"{escapedArgs}\""
            });
        }
    }
}