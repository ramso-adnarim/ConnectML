using System;
using Velopack;

namespace ConnectML.UI
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // O VelopackApp DEVE ser o primeiro código a rodar na aplicação!
            VelopackApp.Build().Run();

            // Inicializa a aplicação WPF manualmente
            var app = new ConnectML.UI.App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
