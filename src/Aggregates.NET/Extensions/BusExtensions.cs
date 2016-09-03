using Aggregates.Exceptions;
using Aggregates.Internal;
using Aggregates.Messages;
using NServiceBus;
using NServiceBus.Logging;
using NServiceBus.Unicast;
using NServiceBus.Unicast.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aggregates.Extensions
{
    public static class BusExtensions
    {
        private static ILog Logger = LogManager.GetLogger("Bus");
        public static Task AsCommandResult(this ICallback callback)
        {
            return callback.Register(x =>
            {
                var reply = x.Messages.FirstOrDefault();
                if (reply is Reject)
                {
                    var reject = reply as Reject;
                    Logger.WriteFormat(LogLevel.Warn, "Command was rejected - Message: {0}\nException: {1}", reject.Message, reject.Exception);
                    if (reject.Exception != null)
                        throw new CommandRejectedException(reject.Message, reject.Exception);
                    else if (reject != null)
                        throw new CommandRejectedException(reject.Message);
                    throw new CommandRejectedException();
                }
                if (reply is Error)
                {
                    var error = reply as Error;
                    Logger.Warn($"Command Fault!\n{error.Message}");
                    throw new CommandRejectedException($"Command Fault!\n{error.Message}");
                }
            });
        }
        

        public static Task Command(this IBus bus, ICommand command)
        {
            // All commands get a response so we'll need to register a callback
            return bus.Send(command).AsCommandResult();
        }
        public static Task Command(this IBus bus, string destination, ICommand command)
        {
            // All commands get a response so we'll need to register a callback
            return bus.Send(destination, command).AsCommandResult();
        }
        public static Task Command(this IBus bus, Address destination, ICommand command)
        {
            // All commands get a response so we'll need to register a callback
            return bus.Send(destination, command).AsCommandResult();
        }
        public static Task Command<TCommand>(this IBus bus, Action<TCommand> command) where TCommand : ICommand
        {
            // All commands get a response so we'll need to register a callback
            return bus.Send(command).AsCommandResult();
        }
        public static Task Command<TCommand>(this IBus bus, string destination, Action<TCommand> command) where TCommand : ICommand
        {
            // All commands get a response so we'll need to register a callback
            return bus.Send(destination, command).AsCommandResult();
        }
        public static Task Command<TCommand>(this IBus bus, Address destination, Action<TCommand> command) where TCommand : ICommand
        {
            // All commands get a response so we'll need to register a callback
            return bus.Send(destination, command).AsCommandResult();
        }

        /// <summary>
        /// Send the command, don't care if its rejected
        /// </summary>
        /// <param name="bus"></param>
        /// <param name="command"></param>
        /// <returns></returns>
        public static async Task PassiveCommand<TCommand>(this IBus bus, Action<TCommand> command) where TCommand : ICommand
        {
            try
            {
                await bus.Send(command).AsCommandResult();
            }
            catch (CommandRejectedException) { }
        }
        public static async Task PassiveCommand(this IBus bus, ICommand command)
        {
            try
            {
                await bus.Send(command).AsCommandResult();
            }
            catch (CommandRejectedException) { }
        }
        public static async Task PassiveCommand<TCommand>(this IBus bus, string destination, Action<TCommand> command) where TCommand : ICommand
        {
            try
            {
                await bus.Send(destination, command).AsCommandResult();
            }
            catch (CommandRejectedException) { }
        }
        public static async Task PassiveCommand<TCommand>(this IBus bus, Address destination, Action<TCommand> command) where TCommand : ICommand
        {
            try
            {
                await bus.Send(destination, command).AsCommandResult();
            }
            catch (CommandRejectedException) { }
        }
        public static async Task PassiveCommand(this IBus bus, string destination, ICommand command)
        {
            try
            {
                await bus.Send(destination, command).AsCommandResult();
            }
            catch (CommandRejectedException) { }
        }
        public static async Task PassiveCommand(this IBus bus, Address destination, ICommand command)
        {
            try
            {
                await bus.Send(destination, command).AsCommandResult();
            }
            catch (CommandRejectedException) { }
        }


        public static void ReplyAsync(this IHandleContext context, object message)
        {
            var incoming = context.Context.PhysicalMessage;
            context.Bus.SetMessageHeader(message, "$.Aggregates.Replying", "1");
            var replyTo = incoming.ReplyToAddress.ToString();
            // Special case if using RabbitMq - replies need to be sent to the CallbackQueue NOT the primary queue
            if (context.Context.PhysicalMessage.Headers.ContainsKey("NServiceBus.RabbitMQ.CallbackQueue"))
                replyTo = context.Context.PhysicalMessage.Headers["NServiceBus.RabbitMQ.CallbackQueue"];


            context.Bus.Send(Address.Parse(replyTo), String.IsNullOrEmpty(incoming.CorrelationId) ? incoming.Id : incoming.CorrelationId, message);
        }
        public static void ReplyAsync<T>(this IHandleContext context, Action<T> message)
        {
            context.ReplyAsync(context.Mapper.CreateInstance(message));
        }
        public static void PublishAsync<T>(this IHandleContext context, Action<T> message)
        {
            context.Bus.Publish<T>(message);
        }
        public static void PublishAsync(this IHandleContext context, object message)
        {
            context.Bus.Publish(message);
        }
        public static void SendAsync<T>(this IHandleContext context, Action<T> message)
        {
            context.Bus.Send(message);
        }
        public static void SendAsync(this IHandleContext context, object message)
        {
            context.Bus.Send(message);
        }
        public static void SendAsync<T>(this IHandleContext context, String destination, Action<T> message)
        {
            context.Bus.Send(destination, message);
        }
        public static void SendAsync(this IHandleContext context, String destination, object message)
        {
            context.Bus.Send(destination, message);
        }
    }
}
