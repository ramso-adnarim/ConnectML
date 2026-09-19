using System;
using System.Threading.Tasks;
using System.Windows;
using Serilog;

namespace ConnectML.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Captura de exceções não tratadas na UI thread (Dispatcher do WPF)
            DispatcherUnhandledException += (sender, args) =>
            {
                string errorMsg = $"[FATAL DISPATCHER] {args.Exception}";
                Console.Error.WriteLine(errorMsg);
                Log.Fatal(args.Exception, "Exceção não tratada no Dispatcher da UI: {Message}", args.Exception.Message);
                Log.CloseAndFlush();
            };

            // 2. Captura de exceções não tratadas em qualquer thread de background
            AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    string errorMsg = $"[FATAL APPDOMAIN] {ex}";
                    Console.Error.WriteLine(errorMsg);
                    Log.Fatal(ex, "Exceção fatal não tratada no AppDomain: {Message}", ex.Message);
                    Log.CloseAndFlush();
                }
            };

            // 3. Captura de exceções não observadas em Tasks assíncronas
            TaskScheduler.UnobservedTaskException += (sender, args) =>
            {
                string errorMsg = $"[UNOBSERVED TASK] {args.Exception}";
                Console.Error.WriteLine(errorMsg);
                Log.Error(args.Exception, "Exceção não observada em Task assíncrona: {Message}", args.Exception.Message);
                args.SetObserved();
            };
        }
    }
}

