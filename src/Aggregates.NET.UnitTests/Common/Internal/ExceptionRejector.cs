namespace Aggregates.NET.UnitTests.Common.Internal
{
    //[TestFixture]
    //public class ExceptionRejector
    //{
    //    private Aggregates.Internal.ExceptionRejector _rejector;

    //    [SetUp]
    //    public void Setup()
    //    {
    //        var settings = new Moq.Mock<ReadOnlySettings>();
    //        settings.Setup(x => x.Get<Int32>("MaxRetries")).Returns(2);

    //        _rejector = new Aggregates.Internal.ExceptionRejector(settings.Object);
    //    }

    //    [Test]
    //    public async Task no_problem()
    //    {
    //        var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
    //        var next = new Moq.Mock<Func<Task>>();
    //        context.Setup(x => x.MessageId).Returns("1");

    //        await _rejector.Invoke(context.Object, next.Object);
    //        next.Verify(x => x(), Moq.Times.Once);
    //    }
    //    [Test]
    //    public async Task is_retry_sets_headers()
    //    {
    //        var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
    //        var next = new Moq.Mock<Func<Task>>();
    //        context.Setup(x => x.MessageId).Returns("1");
    //        context.Setup(x => x.Message.Body).Returns(new byte[] { });
    //        next.Setup(x => x()).Throws(new Exception("test"));

    //        context.Setup(x => x.Extensions.Set(Defaults.RETRIES, 1)).Verifiable();

    //        Assert.ThrowsAsync<Exception>(async () => await _rejector.Invoke(context.Object, next.Object));

    //        next.Verify(x => x(), Moq.Times.Once);

    //        next.Setup(x => x());
    //        await _rejector.Invoke(context.Object, next.Object);
    //        next.Verify(x => x(), Moq.Times.Exactly(2));

    //        context.Verify(x => x.Extensions.Set(Defaults.RETRIES, 1), Moq.Times.Once);

    //    }
    //    [Test]
    //    public Task max_retries()
    //    {
    //        var context = new Moq.Mock<IIncomingPhysicalMessageContext>();
    //        var next = new Moq.Mock<Func<Task>>();
    //        var errorFunc = new Moq.Mock<Func<Exception, String, Error>>();
    //        errorFunc.Setup(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>())).Returns(new Moq.Mock<Error>().Object).Verifiable();
    //        context.Setup(x => x.Builder.Build<Func<Exception, String, Error>>()).Returns(errorFunc.Object);

    //        context.Setup(x => x.MessageId).Returns("1");
    //        context.Setup(x => x.Message.Body).Returns(new byte[] { });
    //        context.Setup(x => x.Message.Headers[Headers.MessageIntent]).Returns(MessageIntentEnum.Send.ToString());
    //        next.Setup(x => x()).Throws(new Exception("test"));

    //        context.Setup(x => x.Extensions.Set(Defaults.RETRIES, 1)).Verifiable();
            
    //        Assert.ThrowsAsync<Exception>(async () => await _rejector.Invoke(context.Object, next.Object));
    //        Assert.ThrowsAsync<Exception>(async () => await _rejector.Invoke(context.Object, next.Object));
    //        Assert.DoesNotThrowAsync(async () => await _rejector.Invoke(context.Object, next.Object));

    //        next.Verify(x => x(), Moq.Times.Exactly(3));

    //        context.Verify(x => x.Extensions.Set(Defaults.RETRIES, 1), Moq.Times.Once);
    //        context.Verify(x => x.Extensions.Set(Defaults.RETRIES, 2), Moq.Times.Once);
    //        context.Verify(x => x.Extensions.Set(Defaults.RETRIES, 3), Moq.Times.Never);

    //        errorFunc.Verify(x => x(Moq.It.IsAny<Exception>(), Moq.It.IsAny<string>()), Moq.Times.Once);
    //        context.Verify(x => x.Reply(Moq.It.IsAny<object>()), Moq.Times.Once);

    //        return Task.CompletedTask;
    //    }
    //}
}
