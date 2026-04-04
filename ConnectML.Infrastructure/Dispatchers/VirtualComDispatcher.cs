using System;
using System.IO.Ports;
using System.Threading.Tasks;
using ConnectML.Core.Interfaces;
using Serilog;

namespace ConnectML.Infrastructure.Dispatchers
{
    public class VirtualComDispatcher : IInboundDispatcher
    {
        private readonly string _comPort;

        public VirtualComDispatcher(string comPort)
        {
            _comPort = comPort;
        }

        public async Task DispatchIncomingPayloadAsync(string payload)
        {
            // Executamos a tarefa no background para não bloquear requisições HTTP
            await Task.Run(() => 
            {
                try
                {
                    Log.Information($"[VirtualComDispatcher] Enviando pacote via porta {_comPort}...");
                    
                    if (string.IsNullOrEmpty(_comPort) || _comPort == "OFF")
                    {
                        Log.Warning("[VirtualComDispatcher] Nenhuma porta COM configurada para o repasse.");
                        return;
                    }

                    using (var serialPort = new SerialPort(_comPort))
                    {
                        serialPort.BaudRate = 9600;
                        serialPort.Open();
                        serialPort.WriteLine(payload); // Despacho via porta serial
                    }
                    
                    Log.Information($"[VirtualComDispatcher] Sucesso ao enviar para a {_comPort}.");
                }
                catch (Exception ex)
                {
                    Log.Error($"[VirtualComDispatcher] Falha de comunicação na porta {_comPort}: {ex.Message}");
                }
            });
        }
    }
}
