﻿using Moq;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using VSRAD.DebugServer.IPC.Commands;
using VSRAD.DebugServer.IPC.Responses;
using VSRAD.Package.ProjectSystem;
using VSRAD.Package.ProjectSystem.Macros;
using VSRAD.Package.Server;
using VSRAD.PackageTests;
using Xunit;

namespace VSRAD.Package.BuildTools
{
    public class BuildToolsServerTest
    {
        [Fact]
        public async Task BuildTestAsync()
        {
            TestHelper.InitializePackageTaskFactory();
            var project = TestHelper.MakeProjectWithProfile(new Dictionary<string, string>()
            {
                { RadMacros.BuildExecutable, "nemu" },
                { RadMacros.BuildArguments, "--sleep 10" },
                { RadMacros.BuildWorkingDirectory, "/old/home" }
            },
            projectRoot: @"C:\Users\CFF");
            var channel = new MockCommunicationChannel();
            var output = new Mock<IOutputWindowManager>();
            var deployManager = new Mock<IFileSynchronizationManager>();
            output.Setup((w) => w.GetExecutionResultPane()).Returns(new Mock<IOutputWindowWriter>().Object);

            var server = new BuildToolsServer(channel.Object, output.Object, deployManager.Object);
            server.SetProjectOnLoad(project); // starts the server

            channel.ThenRespond<Execute, ExecutionCompleted>(new ExecutionCompleted
            {
                Status = ExecutionStatus.Completed,
                ExitCode = 0,
                Stdout = "day of flight",
                Stderr = "coming soon"
            },
            (command) =>
            {
                Assert.Equal("nemu", command.Executable);
                Assert.Equal("--sleep 10", command.Arguments);
                Assert.Equal("/old/home", command.WorkingDirectory);
            });

            VSRAD.BuildTools.IPCBuildResult message = null;
            var tcs = new TaskCompletionSource<bool>();

            new Thread(() =>
            {
                var client = new NamedPipeClientStream(server.PipeName);
                client.Connect();
                message = VSRAD.BuildTools.IPCBuildResult.Read(client);
                client.Close();
                tcs.SetResult(true);
            }).Start();

            await tcs.Task;
            deployManager.Verify((d) => d.SynchronizeRemoteAsync(), Times.Once);

            Assert.Null(message.ServerError);
            Assert.Equal(0, message.ExitCode);
            Assert.Equal("day of flight", message.Stdout);
            Assert.Equal("coming soon", message.Stderr);
        }
    }
}
