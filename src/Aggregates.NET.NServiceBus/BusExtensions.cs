using System;
using System.Threading;
using System.Threading.Tasks;
using Aggregates.Exceptions;
using Aggregates.Messages;
using NServiceBus;
using ICommand = Aggregates.Messages.ICommand;
using IMessage = Aggregates.Messages.IMessage;
using Aggregates.Extensions;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Aggregates
{
    [ExcludeFromCodeCoverage]
    public static class BusExtensions
    {

        private static void CheckResponse(ILogger Logger, ICommand command, IMessage msg)
        {
            if (msg is Reject)
            {
                var reject = (Reject)msg;
                Logger.WarnEvent("Response", $"Command was rejected - Message: {reject.Message}");
                throw new RejectedException(command.GetType(), reject.Message, reject.Exception);
            }
            if (msg is Error)
            {
                var error = (Error)msg;
                Logger.WarnEvent("Response", $"Command Fault!\n{error.Message}");
                throw new RejectedException(command.GetType(), $"Command Fault!\n{error.Message}");
            }
        }
        public static async Task Command(this IMessageSession ctx, ILogger Logger, string destination, ICommand command)
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "1");

            var response = await ctx.Request<IMessage>(command, options).ConfigureAwait(false);
            CheckResponse(Logger, command, response);
        }

        public static async Task<bool> TimeoutCommand(this IMessageSession ctx, ILogger Logger, string destination, ICommand command, TimeSpan timeout)
        {
            var options = new SendOptions();
            options.SetDestination(destination);
            options.SetHeader(Defaults.RequestResponse, "1");

            var cancelation = new CancellationTokenSource(timeout);
            try
            {
                var response = await ctx.Request<IMessage>(command, options, cancelation.Token).ConfigureAwait(false);
                CheckResponse(Logger, command, response);
                return true;
            }
            catch (TaskCanceledException)
            {
                Logger.WarnEvent("TimeOut", "{CommandType}", command.GetType().FullName);
                return false;
            }
        }

        /// <summary>
        /// Send the command, don't care if its rejected
        /// </summary>
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
            if (string.IsNullOrEmpty(ctx.GetSettings()?.CommandDestination))
                throw new ArgumentException($"Must use Configuration.SetCommandDestination to use destination-less extension methods");

            var options = new SendOptions();
            options.SetDestination(ctx.GetSettings().CommandDestination);
            options.SetHeader(Defaults.RequestResponse, "0");

            await ctx.Send(command, options).ConfigureAwait(false);
        }
        public static async Task PassiveCommand(this IMessageHandlerContext ctx, ICommand command)
        {
            if (string.IsNullOrEmpty(ctx.GetSettings()?.CommandDestination))
                throw new ArgumentException($"Must use Configuration.SetCommandDestination to use destination-less extension methods");

            var options = new SendOptions();
            options.SetDestination(ctx.GetSettings().CommandDestination);
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
