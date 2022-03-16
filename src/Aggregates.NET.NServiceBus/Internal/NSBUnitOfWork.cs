using Aggregates.Contracts;
using Aggregates.Extensions;
using Microsoft.Extensions.Logging;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    public class NSBUnitOfWork : UnitOfWork, IMutate
    {
        private readonly IVersionRegistrar _registrar;

        public NSBUnitOfWork(ILogger<NSBUnitOfWork> logger, IRepositoryFactory repoFactory, IEventFactory eventFactory, IVersionRegistrar registrar) : base(logger, repoFactory)
        {
            _registrar = registrar;
        }


        public override IMutating MutateIncoming(IMutating command)
        {
            CurrentMessage = command.Message;

            // There are certain headers that we can make note of
            // These will be committed to the event stream and included in all .Reply or .Publish done via this Unit Of Work
            // Meaning all receivers of events from the command will get information about the command's message, if they care
            foreach (var header in NSBDefaults.CarryOverHeaders)
            {
                var defaultHeader = "";
                command.Headers.TryGetValue(header, out defaultHeader);

                if (string.IsNullOrEmpty(defaultHeader))
                    defaultHeader = NotFound;

                var workHeader = $"{Defaults.OriginatingHeader}.{header}";
                CurrentHeaders[workHeader] = defaultHeader;
            }

            Type type = null;
            if (command.Headers.TryGetValue(Headers.EnclosedMessageTypes, out var messageType))
            {
                if (messageType.IndexOf(';') != -1)
                    messageType = messageType.Substring(0, messageType.IndexOf(';'));
                type = Type.GetType(messageType, false);
            }

            CurrentHeaders[Defaults.OriginatingMessageHeader] = type == null ? "<UNKNOWN>" : _registrar.GetVersionedName(type);


            // Copy any application headers the user might have included
            var userHeaders = command.Headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.RequestResponse, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.Retries, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.LocalHeader, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.BulkHeader, StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                CurrentHeaders[header] = command.Headers[header];

            if (command.Headers.ContainsKey(Headers.CorrelationId))
                CurrentHeaders[$"{Defaults.PrefixHeader}.{Defaults.CorrelationIdHeader}"] = command.Headers[Headers.CorrelationId];

            string messageId;
            Guid commitId = Guid.NewGuid();

            // Attempt to get MessageId from NServicebus headers
            // If we maintain a good CommitId convention it should solve the message idempotentcy issue (assuming the storage they choose supports it)
            if (command.Headers.TryGetValue(Headers.MessageId, out messageId))
                Guid.TryParse(messageId, out commitId);
            if (command.Headers.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.MessageIdHeader}", out messageId))
                Guid.TryParse(messageId, out commitId);

            // Allow the user to send a CommitId along with his message if he wants
            if (command.Headers.TryGetValue($"{Defaults.PrefixHeader}.{Defaults.CommitIdHeader}", out messageId))
                Guid.TryParse(messageId, out commitId);


            CommitId = commitId;

            // Helpful log and gets CommitId into the dictionary
            var firstEventId = UnitOfWork.NextEventId(CommitId);

            return command;
        }

        public override IMutating MutateOutgoing(IMutating command)
        {
            foreach (var header in CurrentHeaders)
                command.Headers[header.Key] = header.Value;

            return command;
        }
    }
}
