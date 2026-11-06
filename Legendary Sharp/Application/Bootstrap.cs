using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class Bootstrap
{
    public static async Task<int> RunAsync(string[] args)
    {
        ConsoleEx.Initialise();

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Interrupt.Signal();
        };

        if (args.Length > 0) return await CommandLine.RunAsync(args).ConfigureAwait(false);

        try
        {
            Console.Title = AppInfo.Name;
        }
        catch (IOException)
        {
        }

        if (!ConsoleEx.IsInteractive)
        {
            Logo.Render(AppInfo.Tagline);
            Output.Blank();
            Output.Warn($"The menu needs a real console window. Scripts can use commands, see {AppInfo.Command} --help.");
            Output.Hint("Double click the exe, or run it from Terminal, PowerShell or cmd.");
            Output.Blank();
            return CommandLine.UsageError;
        }

        ConsoleEx.EnterAppScreen();

        try
        {
            using var session = Session.Create();
            ConsoleEx.ApplyPointer(session.Settings.Pointer);

            var code = await new InteractiveShell(session).RunAsync().ConfigureAwait(false);

            ConsoleEx.LeaveAppScreen();
            Output.Line(Theme.Paint("Thanks for using " + AppInfo.Name + " v" + AppInfo.Version + ", made by " + AppInfo.Author + ".", Theme.Muted));
            Output.Line(Theme.Paint(AppInfo.Repository, Theme.Dim));
            return code;
        }
        catch (OperationCanceledException)
        {
            ConsoleEx.LeaveAppScreen();
            Output.Warn("Cancelled.");
            return 130;
        }
        catch (Exception error)
        {
            ConsoleEx.LeaveAppScreen();
            Output.Blank();
            Output.Error(error.Message);
            if (ErrorLog.Write(error, "startup") is { } log) Output.Hint($"Details were saved to {log}");

            if (Environment.GetEnvironmentVariable("LGS_DEBUG") is not null)
                Output.Detail(error.ToString());

            ConsoleEx.PauseForKey("Press any key to close.");
            return 1;
        }
        finally
        {
            ConsoleEx.LeaveAppScreen();
            ConsoleEx.ShowCursor();
            ConsoleInput.Restore();
        }
    }
}
