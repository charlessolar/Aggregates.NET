using Aggregates;
using NServiceBus;
using Shared;

namespace Client
{
    internal class EchoService
    {
        private readonly IMessageSession _session;
        private readonly ISettings _settings;

        public EchoService(IMessageSession session, ISettings settings)
        {
            _session = session;
            _settings = settings;
        }

        public Task SendMessage(string message)
        {
            var command = new Send {
                Message = message
        };

            return _session.Command(_settings.CommandDestination, command);
        }

        public Task<MessageTotalResponse> RequestTotal()
        {
            var options = new SendOptions();
            options.SetDestination(_settings.CommandDestination);
            return _session.Request<MessageTotalResponse>(new MessageTotal(), options);
        }
    }
}
