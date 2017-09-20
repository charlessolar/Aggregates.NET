using System;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Logging;
using Aggregates.Messages;
using NServiceBus;
using ICommand = Aggregates.Messages.ICommand;
using IMessage = Aggregates.Messages.IMessage;
using Aggregates.Extensions;

namespace Aggregates
{
    public static class BusExtensions
    {
        private static readonly ILog Logger = LogProvider.GetLogger("Command");

        public static async Task Command(this IMessageSession ctx, ICommand command)
        {
            var options = new SendOptions();
            options.SetHeader(Defaults.RequestResponse, "1");

            var response = await ctx.Request<IMessage>(command, options).ConfigureAwait(false);
            response.CommandResponse();
        }
        public static async Task Command(this IMessageSession ctx, string destination, ICommand command)
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "1");

            var response = await ctx.Request<IMessage>(command, options).ConfigureAwait(false);
            response.CommandResponse();
        }

        public static async Task<bool> TimeoutCommand(this IMessageSession ctx, ICommand command, TimeSpan timeout)
        {
            var options = new SendOptions();
            options.SetHeader(Defaults.RequestResponse, "1");

            var cancelation = new CancellationTokenSource(timeout);
            try
            {
                var response = await ctx.Request<IMessage>(command, options, cancelation.Token).ConfigureAwait(false);
                response.CommandResponse();
                return true;
            }
            catch (TaskCanceledException)
            {
                Logger.Warn($"Command {command.GetType().FullName} timed out");
                return false;
            }
        }
        public static async Task<bool> TimeoutCommand(this IMessageSession ctx, string destination, ICommand command, TimeSpan timeout)
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "1");

            var cancelation = new CancellationTokenSource(timeout);
            try
            {
                var response = await ctx.Request<IMessage>(command, options, cancelation.Token).ConfigureAwait(false);
                response.CommandResponse();
                return true;
            }
            catch (TaskCanceledException)
            {
                Logger.Warn($"Command {command.GetType().FullName} timed out");
                return false;
            }
        }

        /// <summary>
        /// Send the command, don't care if its rejected
        /// </summary>
        public static async Task PassiveCommand<TCommand>(this IMessageSession ctx, Action<TCommand> command) where TCommand : ICommand
        {
            var options = new SendOptions();
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand(this IMessageSession ctx, ICommand command)
        {
            var options = new SendOptions();
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand<TCommand>(this IMessageSession ctx, string destination, Action<TCommand> command) where TCommand : ICommand
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand(this IMessageSession ctx, string destination, ICommand command)
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand<TCommand>(this IMessageHandlerContext ctx, Action<TCommand> command) where TCommand : ICommand
        {
            var options = new SendOptions();
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand(this IMessageHandlerContext ctx, ICommand command)
        {
            var options = new SendOptions();
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand<TCommand>(this IMessageHandlerContext ctx, string destination, Action<TCommand> command) where TCommand : ICommand
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand(this IMessageHandlerContext ctx, string destination, ICommand command)
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
    }
}
