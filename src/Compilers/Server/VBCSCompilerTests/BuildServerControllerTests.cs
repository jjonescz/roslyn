// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CommandLine;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.CodeAnalysis.CompilerServer.UnitTests
{
    public sealed class BuildServerControllerTests : IDisposable
    {
        public void Dispose()
        {
            NamedPipeTestUtil.DisposeAll();
        }

        public sealed class GetDefaultKeepAliveTests
        {
            private readonly NameValueCollection _appSettings = new NameValueCollection();
            private readonly XunitCompilerServerLogger _logger;

            public GetDefaultKeepAliveTests(ITestOutputHelper testOutputHelper)
            {
                _logger = new XunitCompilerServerLogger(testOutputHelper);
            }

            [Theory]
            [InlineData(null, 600)]
            [InlineData("1", 1)]
            [InlineData("3600", 3600)]
            [InlineData("0", -1)]
            [InlineData("  +42 ", 42)]
            [InlineData("2147483", 2147483)]
            public void EnvironmentSetting(string value, int expectedSeconds)
            {
                var environment = new TestableBuildEnvironment(Path.GetTempPath());
                if (value is not null)
                {
                    environment.EnvironmentVariables[BuildServerController.KeepAliveEnvironmentVariable] = value;
                }

                var result = BuildServerController.GetDefaultKeepAlive(_logger, _appSettings, environment);
                var expected = expectedSeconds == -1 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(expectedSeconds);
                Assert.Equal(expected, result);
                Assert.Empty(_logger.GetMessagesSnapshot());

                // Validate the timer range without waiting for the delay.
                Assert.True(Task.Delay(result, new CancellationToken(canceled: true)).IsCanceled);
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData(" ")]
            [InlineData("-1")]
            [InlineData("2147484")]
            [InlineData("2147483647")]
            [InlineData("2147483648")]
            [InlineData("invalid")]
            [InlineData("1.5")]
            [InlineData("1,000")]
            public void InvalidOrUnsetEnvironmentFallsBackToAppSettings(string value)
            {
                var environment = new TestableBuildEnvironment(Path.GetTempPath());
                if (value is not null)
                {
                    environment.EnvironmentVariables[BuildServerController.KeepAliveEnvironmentVariable] = value;
                }

                _appSettings[BuildServerController.KeepAliveSettingName] = "42";
                Assert.Equal(TimeSpan.FromSeconds(42), BuildServerController.GetDefaultKeepAlive(_logger, _appSettings, environment));
                if (value is null)
                {
                    Assert.Empty(_logger.GetMessagesSnapshot());
                }
                else
                {
                    Assert.Equal(
                        $"Invalid ROSLYN_COMPILER_SERVER_KEEPALIVE_SECONDS='{value}': expected an integer from 0 to 2147483. Using the default keep alive timeout.",
                        Assert.Single(_logger.GetMessagesSnapshot()));
                }

                _appSettings.Clear();
                Assert.Equal(ServerDispatcher.DefaultServerKeepAlive, BuildServerController.GetDefaultKeepAlive(_logger, _appSettings, environment));
            }

            [Theory]
            [InlineData("3600", 3600)]
            [InlineData("0", -1)]
            public void EnvironmentOverridesAppSettings(string value, int expectedSeconds)
            {
                var environment = new TestableBuildEnvironment(Path.GetTempPath());
                environment.EnvironmentVariables[BuildServerController.KeepAliveEnvironmentVariable] = value;
                _appSettings[BuildServerController.KeepAliveSettingName] = "42";

                var expected = expectedSeconds == -1 ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(expectedSeconds);
                Assert.Equal(expected, BuildServerController.GetDefaultKeepAlive(_logger, _appSettings, environment));
                Assert.Empty(_logger.GetMessagesSnapshot());
            }

            [Fact]
            public void Simple()
            {
                _appSettings[BuildServerController.KeepAliveSettingName] = "42";
                Assert.Equal(TimeSpan.FromSeconds(42), BuildServerController.GetDefaultKeepAlive(EmptyCompilerServerLogger.Instance, _appSettings));
            }

            [Fact]
            public void InvalidNumber()
            {
                _appSettings[BuildServerController.KeepAliveSettingName] = "dog";
                Assert.Equal(ServerDispatcher.DefaultServerKeepAlive, BuildServerController.GetDefaultKeepAlive(EmptyCompilerServerLogger.Instance, _appSettings));
            }

            [Fact]
            public void NoSetting()
            {
                Assert.Equal(ServerDispatcher.DefaultServerKeepAlive, BuildServerController.GetDefaultKeepAlive(EmptyCompilerServerLogger.Instance, _appSettings));
            }
        }
    }
}
