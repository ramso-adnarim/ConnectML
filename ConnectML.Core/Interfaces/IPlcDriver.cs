using System.Threading.Tasks;

namespace ConnectML.Core.Interfaces
{
    public interface IPlcDriver
    {
        Task ConnectAsync(string ip, int rack, int slot);
        Task DisconnectAsync();
        Task WriteBoolAsync(string dbAddress, bool value);
        Task WriteIntAsync(string dbAddress, int value);
        bool IsConnected { get; }
    }
}
