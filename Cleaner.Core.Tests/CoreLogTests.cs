using System;
using System.Collections.Generic;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

[Collection("CoreLog")]
public class CoreLogTests
{
    [Fact]
    public void Warn_Reaches_Sink()
    {
        var messages = new List<string>();
        CoreLog.Sink = (level, message, ex) => messages.Add(level + ":" + message);
        try
        {
            CoreLog.Warn("тестовое-сообщение-xyz");
            Assert.Contains("WARN:тестовое-сообщение-xyz", messages);
        }
        finally
        {
            CoreLog.Sink = null;
        }
    }

    [Fact]
    public void Failing_Sink_Does_Not_Throw()
    {
        CoreLog.Sink = (_, _, _) => throw new InvalidOperationException("boom");
        try
        {
            var ex = Record.Exception(() => CoreLog.Error("ошибка", new Exception("x")));
            Assert.Null(ex);
        }
        finally
        {
            CoreLog.Sink = null;
        }
    }

    [Fact]
    public void No_Sink_Does_Not_Throw()
    {
        CoreLog.Sink = null;
        var ex = Record.Exception(() => CoreLog.Info("тест"));
        Assert.Null(ex);
    }
}

[CollectionDefinition("CoreLog", DisableParallelization = true)]
public class CoreLogCollection { }
