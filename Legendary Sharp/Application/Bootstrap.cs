using Legendary_Sharp.Interface;

namespace Legendary_Sharp.Application;

internal static class Bootstrap
{
    public static async Task<int> RunAsync()
    {
        ConsoleEx.Initialise();

        try
        {
            Console.Title = AppInfo.Name;
        }
        catch (IOException)
        {
        }

        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Interrupt.Signal();
        };

        if (!ConsoleEx.IsInteractive)
        {
            Logo.Render(AppInfo.Tagline);
            Output.Blank();
            Output.Warn($"{AppInfo.Name} is an interactive app and needs a real console window.");
            Output.Hint("Double click the exe, or run it from Terminal, PowerShell or cmd.");
            Output.Blank();
            return 2;
        }

        try
        {
            using var session = Session.Create();
            ConsoleEx.ApplyPointer(session.Settings.Pointer);

            var code = await new InteractiveShell(session).RunAsync().ConfigureAwait(false);

            ConsoleEx.Clear();
            Output.Blank();
            Output.Line(Theme.Paint("Thanks for using " + AppInfo.Name + ".", Theme.Muted));
            Output.Blank();
            return code;
        }
        catch (OperationCanceledException)
        {
            Output.Blank();
            Output.Warn("Cancelled.");
            return 130;
        }
        catch (Exception error)
        {
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
            ConsoleEx.ShowCursor();
            ConsoleInput.Restore();
        }
    }
}
