using System.Threading.Tasks;

namespace ConnectML.Core.Interfaces
{
    public interface IOutboundDispatcher
    {
        Task DispatchAsync(bool isOk, int failCount, string product);
    }
}
