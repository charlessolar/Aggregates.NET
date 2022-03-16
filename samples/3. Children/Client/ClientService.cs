using Aggregates;
using NServiceBus;
using Shared;

namespace Client
{
    internal class ClientService
    {
        private readonly IMessageSession _session;
        private readonly ISettings _settings;

        private string _parent = default!;

        public ClientService(IMessageSession session, ISettings settings)
        {
            _session = session;
            _settings = settings;
        }

        public async Task NameParent(string name)
        {
            var command = new NameParent
            {
                Name = name
            };

            // will throw if command rejected
            await _session.Command(_settings.CommandDestination, command);
            // command was accepted
            _parent = name;
        }

        public Task AddChild(string name)
        {
            var command = new NameChild
            {
                ParentName = _parent,
                Name = name,
            };

            return _session.Command(_settings.CommandDestination, command);
        }
    }
}
