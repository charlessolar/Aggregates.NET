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

            return _session.Send(_settings.CommandDestination, command);
        }
    }
}
