using System.Threading.Tasks;

namespace ConnectML.Core.Interfaces
{
    public interface IInboundDispatcher
    {
        Task DispatchIncomingPayloadAsync(string payload);
    }
}
