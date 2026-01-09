using System;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ConnectML.UI.Utils
{
    /// <summary>
    /// A simple adapter to bridge Microsoft.Extensions.Logging to the static Serilog.Log
    /// without setting up a full DI container.
    /// </summary>
    /// <typeparam name="T">The type context for the logger</typeparam>
    public class SerilogLoggerAdapter<T> : ILogger<T>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            return null; // Not implemented / needed for this simple use case
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (formatter == null) return;
            string message = formatter(state, exception);

            switch (logLevel)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                    Serilog.Log.Debug(exception, message);
                    break;
                case LogLevel.Information:
                    Serilog.Log.Information(exception, message);
                    break;
                case LogLevel.Warning:
                    Serilog.Log.Warning(exception, message);
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    Serilog.Log.Error(exception, message);
                    break;
                default:
                    Serilog.Log.Information(exception, message);
                    break;
            }
        }
    }
}
