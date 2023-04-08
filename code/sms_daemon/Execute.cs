using System.Diagnostics;

namespace sms_daemon;

public class ExecuteResult
{
    public string StdOut { get; }
    public string StdErr { get; }
    public int ExitCode { get; }

    public bool Success => ExitCode == 0;

    public ExecuteResult(string stdOut, string stdErr, int exitCode)
    {
        this.StdOut = stdOut;
        this.StdErr = stdErr;
        this.ExitCode = exitCode;
    }
};

public static class Execute
{
    public static ExecuteResult Run(string fileName, string arguments = "")
    {
        var stdOut = "";
        var stdErr = "";
        var startInfo = new ProcessStartInfo();
        startInfo.FileName = fileName;
        startInfo.RedirectStandardOutput = true;
        startInfo.UseShellExecute = false;
        startInfo.Arguments = arguments;

        using (var process = new Process())
        {
            process.StartInfo = startInfo;
            process.OutputDataReceived += (sender, e) => stdOut += e.Data;
            process.ErrorDataReceived += (sender, e) => stdErr += e.Data;
            process.Start();
            process.BeginOutputReadLine();
            process.WaitForExit();

            return new ExecuteResult(stdOut, stdErr, process.ExitCode);
        }
    }
}
