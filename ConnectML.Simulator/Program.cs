using System;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ConnectML.Simulator
{
    class Program
    {
        // Porta padrão do Siemens S7 (RFC1006)
        const int PORT = 102; 

        static async Task Main(string[] args)
        {
            Console.WriteLine("=============================================");
            Console.WriteLine("   ConnectML S7 Simulator (Validation Tool)  ");
            Console.WriteLine("=============================================");
            
            TcpListener server = null;
            try
            {
                // Escuta em qualquer interface de rede (0.0.0.0)
                server = new TcpListener(IPAddress.Any, PORT);
                server.Start();
                
                Console.WriteLine($"[Simulator] Escutando na porta {PORT}...");
                Console.WriteLine($"[Simulator] Configure o ConnectML.UI para conectar em 127.0.0.1 (ou IP desta máquina).");
                Console.WriteLine("Aguardando conexões...\n");

                while (true)
                {
                    TcpClient client = await server.AcceptTcpClientAsync();
                    Console.WriteLine($"[ALERTA] Cliente conectado! IP: {client.Client.RemoteEndPoint}");

                    // Processa cada cliente em uma task separada
                    _ = HandleClientAsync(client);
                }
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.AccessDenied)
                {
                     Console.WriteLine($"\n[ERRO] Acesso Negado à porta {PORT}. Tente executar como ADMINISTRADOR.");
                }
                else
                {
                    Console.WriteLine($"\n[ERRO] SocketException: {e.Message}");
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"\n[ERRO] Exception: {e.Message}");
            }
            finally
            {
                server?.Stop();
            }
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                byte[] buffer = new byte[1024]; // Buffer de leitura
                
                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);

                    if (bytesRead == 0)
                    {
                        Console.WriteLine($"[INFO] Cliente {client.Client.RemoteEndPoint} desconectou.");
                        break;
                    }

                    Console.WriteLine($"\n[RECEBIDO] {bytesRead} bytes de {client.Client.RemoteEndPoint}:");
                    Console.WriteLine(HexDump(buffer, bytesRead));

                    // --- MOCK LOGIC ---
                    if (bytesRead > 5)
                    {
                        // 1. COTP Connection Request (CR)
                        // Byte 5 = PDU Type. 0xE0 = CR (Connect Request)
                        if (buffer[5] == 0xE0)
                        {
                            Console.WriteLine(">> [PROTOCOLO] COTP Connection Request (CR) recebido. Enviando Connection Confirm (CC)...");
                            
                            // Response: COTP Connection Confirm (CC) - 0xD0
                            // TPKT(4) + COTP(18) = 22 bytes
                            byte[] ccPacket = new byte[] 
                            { 
                                0x03, 0x00, 0x00, 0x16, // TPKT v3, len=22
                                0x11, 0xD0, 0x00, 0x01, 0x00, 0x01, 0x00, 0xC0, 0x01, 0x0A, 0xC1, 0x02, 0x01, 0x00, 0xC2, 0x02, 0x01, 0x02 
                            };
                            await stream.WriteAsync(ccPacket, 0, ccPacket.Length);
                            Console.WriteLine("[ENVIADO] COTP Connection Confirm.");
                            continue;
                        }
                    }

                    if (bytesRead > 17)
                    {
                        // 2. S7 Setup Communication
                        // ROSCTR = 0xF0 (Job Setup)
                        if (buffer[17] == 0xF0)
                        {
                            Console.WriteLine(">> [PROTOCOLO] S7 Setup Communication recebido. Enviando ACK...");

                            // Response: S7 Setup Communication ACK
                            // TPKT(4) + COTP(2) + S7 Header(10) + Param(8)
                            // Tamanho total 27 (0x1B)
                            // NOTA: Estrutura simplificada que costuma satisfazer S7NetPlus
                            byte[] setupAck = new byte[]
                            {
                                0x03, 0x00, 0x00, 0x1B, // TPKT
                                0x02, 0xF0, 0x80,       // COTP (DT Data)
                                0x32, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x08, 0x00, 0x00, // S7 Header (ROSCTR=0x03 ACK_DATA)
                                0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0 // Parameters (Negotiated PDU size 960)
                            };
                            await stream.WriteAsync(setupAck, 0, setupAck.Length);
                            Console.WriteLine("[ENVIADO] S7 Setup ACK.");
                            continue;
                        }

                        // 3. S7 Write Request
                        // ROSCTR = 0x05 (User Data / Write)
                        if (buffer[17] == 0x05)
                        {
                            Console.WriteLine(">> [PROTOCOLO] !!! Comando de ESCRITA (Write Var) detectado !!!");
                            
                            // Opcional: Responder Write ACK
                            // Se o S7NetPlus ficar esperando, seria bom mandar. Vamos mandar um ACK genérico.
                            // Mas normalmente em Fire-and-Forget ele não trava tanto se não responder, mas pra evitar erro de timeout:
                            
                            // Vamos tentar responder um ACK simples de Write (Function 0x05)
                            // Apenas para o driver ficar feliz e não dar timeout de leitura
                            // Montar um ACK genérico é chato sem parsear o ID do pacote.
                            // Buffer[11] e Buffer[12] são o "Reference" (ID do pacote)
                            
                            byte refHigh = buffer[11];
                            byte refLow = buffer[12];
                            
                            byte[] writeAck = new byte[]
                            {
                                0x03, 0x00, 0x00, 0x16, // TPKT v3, len=22
                                0x02, 0xF0, 0x80,       // COTP DT
                                0x32, 0x03, 0x00, 0x00, refHigh, refLow, 0x00, 0x02, 0x00, 0x01, 0x00, 0x00, // S7 Ack
                                0x05, // Function Write
                                0x01, // Item count
                                0xFF // Return code (Success = 0xFF in S7 Comm response item usually means Valid)
                            };
                            
                            // Nota: Protocolo exato é complexo, este é um "Best Effort Mock"
                            // Se falhar o driver do client vai dar erro de protocolo, mas ok valida que chegou.
                            
                            await stream.WriteAsync(writeAck, 0, writeAck.Length);
                            Console.WriteLine("[ENVIADO] S7 Write ACK.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO] Erro na conexão com cliente: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        static string HexDump(byte[] bytes, int length)
        {
            if (bytes == null || length == 0) return "";
            
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < length; i++)
            {
                sb.Append($"{bytes[i]:X2} ");
            }
            return sb.ToString().Trim();
        }
    }
}
