using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ConnectML.Simulator
{
    class Program
    {
        // Porta padrão do Siemens S7 (RFC1006)
        const int PORT = 102; 
        
        // Memória virtual (DBs)
        static Dictionary<int, byte[]> DBs = new Dictionary<int, byte[]>();

        static async Task Main(string[] args)
        {
            Console.WriteLine("=============================================");
            Console.WriteLine("   ConnectML S7 Simulator (S7 Server Mock)   ");
            Console.WriteLine("=============================================");

            // Pré-aloca DB1 e DB10 como exemplo (mas aloca dinamicamente depois)
            DBs[1] = new byte[1024];
            DBs[10] = new byte[1024];

            TcpListener server = null;
            try
            {
                server = new TcpListener(IPAddress.Any, PORT);
                server.Start();
                
                Console.WriteLine($"[Simulator] Servidor S7 rodando na porta {PORT}...");
                Console.WriteLine($"[Simulator] Configure o ConnectML.UI para conectar em 127.0.0.1\n");
                
                Console.WriteLine("--- COMANDOS DO CONSOLE INTERATIVO (CLI) ---");
                Console.WriteLine(" Leitura: DB10.1=? ou DB10.DBW0=?");
                Console.WriteLine(" Escrita: DB10.1=1 ou DB10.DBW0=55");
                Console.WriteLine("---------------------------------------------\n");

                // Aceita conexões em background
                _ = Task.Run(() => AcceptClients(server));

                // Loop principal do console
                RunConsoleLoop();
            }
            catch (SocketException e)
            {
                if (e.SocketErrorCode == SocketError.AccessDenied)
                    Console.WriteLine($"\n[ERRO] Acesso Negado à porta {PORT}. Tente executar como ADMINISTRADOR.");
                else
                    Console.WriteLine($"\n[ERRO] SocketException: {e.Message}");
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

        static void RunConsoleLoop()
        {
            while (true)
            {
                var input = Console.ReadLine();
                if (string.IsNullOrWhiteSpace(input)) continue;

                try
                {
                    // Pattern ex: DB10.1=1 ou DB10.1=? ou DB10.DBW0=5
                    var matchBit = Regex.Match(input.ToUpper(), @"^DB(\d+)\.(\d+)(?:\.(\d+))?=(.+)$");
                    var matchWord = Regex.Match(input.ToUpper(), @"^DB(\d+)\.DB[W|B](\d+)=(.+)$");

                    if (matchWord.Success)
                    {
                        int dbNumber = int.Parse(matchWord.Groups[1].Value);
                        int byteIdx = int.Parse(matchWord.Groups[2].Value);
                        string val = matchWord.Groups[3].Value;

                        if (!DBs.ContainsKey(dbNumber)) DBs[dbNumber] = new byte[1024];

                        if (val == "?")
                        {
                            int wordVal = (DBs[dbNumber][byteIdx] << 8) | DBs[dbNumber][byteIdx + 1];
                            Console.WriteLine($"[CLI] DB{dbNumber}.DBW{byteIdx} = {wordVal}");
                        }
                        else if (int.TryParse(val, out int intVal))
                        {
                            DBs[dbNumber][byteIdx] = (byte)(intVal >> 8);
                            DBs[dbNumber][byteIdx + 1] = (byte)(intVal & 0xFF);
                            Console.WriteLine($"[CLI] Escrito {intVal} em DB{dbNumber}.DBW{byteIdx}");
                        }
                    }
                    else if (matchBit.Success)
                    {
                        int dbNumber = int.Parse(matchBit.Groups[1].Value);
                        int byteIdx = int.Parse(matchBit.Groups[2].Value);
                        int bitIdx = matchBit.Groups[3].Success ? int.Parse(matchBit.Groups[3].Value) : 0;
                        string val = matchBit.Groups[4].Value;

                        if (!DBs.ContainsKey(dbNumber)) DBs[dbNumber] = new byte[1024];

                        if (val == "?")
                        {
                            bool bitValue = (DBs[dbNumber][byteIdx] & (1 << bitIdx)) != 0;
                            Console.WriteLine($"[CLI] DB{dbNumber}.DBX{byteIdx}.{bitIdx} = {bitValue}");
                        }
                        else
                        {
                            if (val == "1" || val == "TRUE")
                            {
                                DBs[dbNumber][byteIdx] |= (byte)(1 << bitIdx);
                                Console.WriteLine($"[CLI] Escrito TRUE em DB{dbNumber}.DBX{byteIdx}.{bitIdx}");
                            }
                            else if (val == "0" || val == "FALSE")
                            {
                                DBs[dbNumber][byteIdx] &= (byte)~(1 << bitIdx);
                                Console.WriteLine($"[CLI] Escrito FALSE em DB{dbNumber}.DBX{byteIdx}.{bitIdx}");
                            }
                        }
                    }
                    else
                    {
                        Console.WriteLine("[CLI] Comando inválido. Ex: DB10.1=1, DB10.DBW0=50, DB10.1=?");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine("[CLI] Erro: " + e.Message);
                }
            }
        }

        static async Task AcceptClients(TcpListener server)
        {
            while (true)
            {
                var client = await server.AcceptTcpClientAsync();
                Console.WriteLine($"\n[CONEXÃO] Cliente conectado: {client.Client.RemoteEndPoint}");
                _ = HandleClientAsync(client);
            }
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            try
            {
                var stream = client.GetStream();
                byte[] buffer = new byte[1024];

                while (client.Connected)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    // Handshake 1: COTP CR
                    if (bytesRead > 5 && buffer[5] == 0xE0) 
                    {
                        byte[] ccPacket = { 0x03, 0x00, 0x00, 0x16, 0x11, 0xD0, 0x00, 0x01, 0x00, 0x01, 0x00, 0xC0, 0x01, 0x0A, 0xC1, 0x02, 0x01, 0x00, 0xC2, 0x02, 0x01, 0x02 };
                        await stream.WriteAsync(ccPacket, 0, ccPacket.Length);
                        continue;
                    }

                    // Handshake 2: S7 Setup Communication
                    if (bytesRead > 17 && buffer[17] == 0xF0) 
                    {
                        byte[] setupAck = { 0x03, 0x00, 0x00, 0x1B, 0x02, 0xF0, 0x80, 0x32, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x08, 0x00, 0x00, 0xF0, 0x00, 0x00, 0x01, 0x00, 0x01, 0x03, 0xC0 };
                        await stream.WriteAsync(setupAck, 0, setupAck.Length);
                        continue;
                    }

                    // S7 Read Var
                    if (bytesRead > 17 && buffer[17] == 0x04) 
                    {
                        await ProcessRead(stream, buffer, bytesRead);
                        continue;
                    }

                    // S7 Write Var
                    if (bytesRead > 17 && buffer[17] == 0x05) 
                    {
                        await ProcessWrite(stream, buffer, bytesRead);
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERRO DE CONEXÃO] {ex.Message}");
            }
            finally
            {
                Console.WriteLine($"[CONEXÃO] Cliente desconectado.");
                client.Close();
            }
        }

        static async Task ProcessRead(NetworkStream stream, byte[] req, int length)
        {
            try
            {
                byte refHigh = req[11];
                byte refLow = req[12];
                int dbNumber = (req[25] << 8) | req[26];
                int addrBits = (req[28] << 16) | (req[29] << 8) | req[30];
                int byteAddr = addrBits / 8;
                int readLenBytes = (req[23] << 8) | req[24];
                int tsReq = req[22];

                if (tsReq == 0x01 || tsReq == 0x03) // Se for bit, envia apenas 1 byte
                    readLenBytes = 1;

                if (!DBs.ContainsKey(dbNumber)) DBs[dbNumber] = new byte[1024];

                Console.WriteLine($"[S7 SERVER] Leitura recebida -> DB{dbNumber}, Offset={byteAddr}, Bytes={readLenBytes}");

                byte[] res = new byte[21 + readLenBytes];
                res[0] = 0x03; res[1] = 0x00; 
                res[2] = (byte)(res.Length >> 8); res[3] = (byte)(res.Length & 0xFF);
                res[4] = 0x02; res[5] = 0xF0; res[6] = 0x80;
                res[7] = 0x32; res[8] = 0x03; res[9] = 0x00; res[10] = 0x00;
                res[11] = refHigh; res[12] = refLow;
                res[13] = 0x00; res[14] = 0x02; 
                res[15] = (byte)((readLenBytes + 4) >> 8); res[16] = (byte)((readLenBytes + 4) & 0xFF); 
                
                res[17] = 0x04; res[18] = 0x01; 
                res[19] = 0xFF; 
                res[20] = 0x04; 
                int bitsLen = readLenBytes * 8;
                if (tsReq == 0x01 || tsReq == 0x03) bitsLen = 1;

                res[21] = (byte)(bitsLen >> 8); res[22] = (byte)(bitsLen & 0xFF);

                for (int i = 0; i < readLenBytes; i++)
                {
                    if (byteAddr + i < DBs[dbNumber].Length)
                        res[23 + i] = DBs[dbNumber][byteAddr + i];
                }

                await stream.WriteAsync(res, 0, res.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[S7 SERVER ERRO READ] " + ex.Message);
            }
        }

        static async Task ProcessWrite(NetworkStream stream, byte[] req, int length)
        {
            try
            {
                byte refHigh = req[11];
                byte refLow = req[12];
                int dbNumber = (req[25] << 8) | req[26];
                int addrBits = (req[28] << 16) | (req[29] << 8) | req[30];
                int byteAddr = addrBits / 8;
                int bitAddr = addrBits % 8;
                int reqLen = (req[23] << 8) | req[24];
                int tsReq = req[22];

                if (!DBs.ContainsKey(dbNumber)) DBs[dbNumber] = new byte[1024];

                int paramLen = (req[13] << 8) | req[14];
                int dataStart = 17 + paramLen;
                
                int tsData = req[dataStart + 1];
                int lenBitsData = (req[dataStart + 2] << 8) | req[dataStart + 3];
                
                int byteLenData = lenBitsData / 8;
                if (tsData == 3 || tsData == 1) byteLenData = 1; 
                if (byteLenData == 0) byteLenData = reqLen; 

                byte[] writeData = new byte[byteLenData];
                if (dataStart + 4 + byteLenData <= length)
                {
                    Array.Copy(req, dataStart + 4, writeData, 0, byteLenData);
                }

                if (tsReq == 0x01 || tsReq == 0x03) // Bit
                {
                    bool val = writeData[0] != 0;
                    if (val) DBs[dbNumber][byteAddr] |= (byte)(1 << bitAddr);
                    else DBs[dbNumber][byteAddr] &= (byte)~(1 << bitAddr);
                    Console.WriteLine($"[S7 SERVER] Escrita recebida -> DB{dbNumber}.DBX{byteAddr}.{bitAddr} = {val}");
                }
                else // Byte / Word
                {
                    for (int i = 0; i < byteLenData; i++)
                    {
                        if (byteAddr + i < DBs[dbNumber].Length)
                            DBs[dbNumber][byteAddr + i] = writeData[i];
                    }

                    if (byteLenData == 2) {
                        int valInt = (writeData[0] << 8) | writeData[1];
                        Console.WriteLine($"[S7 SERVER] Escrita recebida -> DB{dbNumber}.DBW{byteAddr} = {valInt}");
                    } else if (byteLenData == 1) {
                        Console.WriteLine($"[S7 SERVER] Escrita recebida -> DB{dbNumber}.DBB{byteAddr} = {writeData[0]}");
                    } else {
                        Console.WriteLine($"[S7 SERVER] Escrita recebida -> DB{dbNumber}, Offset={byteAddr}, Bytes={byteLenData}");
                    }
                }

                // Resposta (ACK)
                byte[] res = new byte[22];
                res[0] = 0x03; res[1] = 0x00; res[2] = 0x00; res[3] = 0x16; 
                res[4] = 0x02; res[5] = 0xF0; res[6] = 0x80;
                res[7] = 0x32; res[8] = 0x03; res[9] = 0x00; res[10] = 0x00; 
                res[11] = refHigh; res[12] = refLow; 
                res[13] = 0x00; res[14] = 0x02; 
                res[15] = 0x00; res[16] = 0x01; 
                res[17] = 0x05; res[18] = 0x01; 
                res[19] = 0xFF; // Success
                
                await stream.WriteAsync(res, 0, res.Length);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[S7 SERVER ERRO WRITE] " + ex.Message);
            }
        }
    }
}
