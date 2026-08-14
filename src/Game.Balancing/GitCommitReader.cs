using System.Diagnostics;

namespace Game.Balancing;

/// <summary>
/// Текущий git-коммит репозитория (Блок 7.3.6) — часть метаданных версии JSON-отчёта, чтобы при
/// повторном анализе можно было понять, каким именно кодом/конфигом получен именно этот отчёт
/// (`docs/BUILD_PLAN.md` Блок 7.3.6: «путь конфига, git commit, дата»). Best-effort — не git-репозиторий
/// или отсутствующий <c>git</c> в PATH не должны ронять сам прогон балансировки.
/// </summary>
internal static class GitCommitReader
{
    public static string? TryGetCurrentCommit()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "rev-parse HEAD",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return process.ExitCode == 0 && output.Length > 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
