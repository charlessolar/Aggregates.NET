using Aggregates.Contracts;
using Aggregates.Extensions;
using Aggregates.Logging;
using NServiceBus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Aggregates.Internal
{
    class NSBUnitOfWork : UnitOfWork, IMutate
    {
        public NSBUnitOfWork(IRepositoryFactory repoFactory, IEventFactory eventFactory, IProcessor processor) : base(repoFactory, eventFactory, processor) { }


        public IMutating MutateIncoming(IMutating command)
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

                var workHeader = $"{Defaults.PrefixHeader}.{header}";
                CurrentHeaders[workHeader] = defaultHeader;
            }
            CurrentHeaders[$"{Defaults.PrefixHeader}.OriginatingType"] = CurrentMessage.GetType().FullName;

            // Copy any application headers the user might have included
            var userHeaders = command.Headers.Keys.Where(h =>
                            !h.Equals("CorrId", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals("WinIdName", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("NServiceBus", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.StartsWith("$", StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(CommitHeader) &&
                            !h.Equals(Defaults.CommitIdHeader, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.RequestResponse, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.Retries, StringComparison.InvariantCultureIgnoreCase) &&
                            !h.Equals(Defaults.EventHeader, StringComparison.InvariantCultureIgnoreCase));

            foreach (var header in userHeaders)
                CurrentHeaders[header] = command.Headers[header];

            CurrentHeaders[Defaults.InstanceHeader] = Defaults.Instance.ToString();
            if (command.Headers.ContainsKey(Headers.CorrelationId))
                CurrentHeaders[Headers.CorrelationId] = command.Headers[Headers.CorrelationId];

            string messageId;
            Guid commitId = Guid.NewGuid();

            // Attempt to get MessageId from NServicebus headers
            // If we maintain a good CommitId convention it should solve the message idempotentcy issue (assuming the storage they choose supports it)
            if (CurrentHeaders.TryGetValue(NSBDefaults.MessageIdHeader, out messageId))
                Guid.TryParse(messageId, out commitId);
            if (CurrentHeaders.TryGetValue($"{Defaults.PrefixHeader}.{NSBDefaults.MessageIdHeader}", out messageId))
                Guid.TryParse(messageId, out commitId);
            if (CurrentHeaders.TryGetValue($"{Defaults.DelayedPrefixHeader}.{NSBDefaults.MessageIdHeader}", out messageId))
                Guid.TryParse(messageId, out commitId);

            // Allow the user to send a CommitId along with his message if he wants
            if (CurrentHeaders.TryGetValue(Defaults.CommitIdHeader, out messageId))
                Guid.TryParse(messageId, out commitId);

            CommitId = commitId;

            // Helpful log and gets CommitId into the dictionary
            var firstEventId = UnitOfWork.NextEventId(CommitId);
            Logger.Write(LogLevel.Debug, () => $"Starting unit of work - first event id {firstEventId}");

            return command;
        }

        public IMutating MutateOutgoing(IMutating command)
        {
            foreach (var header in CurrentHeaders)
                command.Headers[header.Key] = header.Value;

            return command;
        }
    }
}
