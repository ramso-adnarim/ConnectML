using System.Threading.Tasks;

namespace ConnectML.Core.Interfaces
{
    public interface IPlcDriver
    {
        Task ConnectAsync(string ip, int rack, int slot, string cpuType = "S71500");
        Task DisconnectAsync();
        Task WriteBoolAsync(string dbAddress, bool value);
        Task WriteIntAsync(string dbAddress, int value);
        Task WriteStringAsync(string dbAddress, string value);
        Task<bool> ReadBoolAsync(string dbAddress);
        Task<int> ReadIntAsync(string dbAddress);
        Task<string> ReadStringAsync(string dbAddress);
        bool IsConnected { get; }
    }
}
