﻿using Microsoft;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Composition;
using VSRAD.DebugServer.IPC.Responses;
using Task = System.Threading.Tasks.Task;

namespace VSRAD.Package.ProjectSystem
{
    public interface IOutputWindowManager
    {
        IOutputWindowWriter GetServerPane();
        IOutputWindowWriter GetExecutionResultPane();
    }

    public interface IOutputWindowWriter
    {
        Task PrintMessageAsync(string title, string contents = null);
    }

    [Export(typeof(IOutputWindowManager))]
    [AppliesTo(Constants.ProjectCapability)]
    public sealed class OutputWindowManager : IOutputWindowManager
    {
        private readonly SVsServiceProvider _serviceProvider;

        [ImportingConstructor]
        public OutputWindowManager(SVsServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public IOutputWindowWriter GetServerPane() => new OutputWindowWriter(_serviceProvider,
            Constants.OutputPaneServerGuid, Constants.OutputPaneServerTitle);

        public IOutputWindowWriter GetExecutionResultPane() => new OutputWindowWriter(_serviceProvider,
            Constants.OutputPaneExecutionResultGuid, Constants.OutputPaneExecutionResultTitle);
    }

    public sealed class OutputWindowWriter : IOutputWindowWriter
    {
        private readonly SVsServiceProvider _serviceProvider;
        private readonly Guid _paneGuid;
        private readonly string _paneTitle;

        private IVsOutputWindowPane _pane;

        public OutputWindowWriter(SVsServiceProvider provider, Guid outputPaneGuid, string outputPaneTitle)
        {
            _serviceProvider = provider;
            _paneGuid = outputPaneGuid;
            _paneTitle = outputPaneTitle;
        }

        public async Task PrintMessageAsync(string title, string contents = null)
        {
            if (_pane == null)
            {
                await VSPackage.TaskFactory.SwitchToMainThreadAsync();
                var outputWindow = _serviceProvider.GetService(typeof(SVsOutputWindow)) as IVsOutputWindow;
                Assumes.Present(outputWindow);
                outputWindow.CreatePane(_paneGuid, _paneTitle, fInitVisible: 1, fClearWithSolution: 1);
                outputWindow.GetPane(_paneGuid, out _pane);
            }

            var message = contents == null
                ? "=== " + title + Environment.NewLine + Environment.NewLine
                : "=== " + title + Environment.NewLine + contents + Environment.NewLine + Environment.NewLine;
            _pane.OutputStringThreadSafe(message);
        }
    }

    public static class OutputWindowExtensions
    {
        public static OutputWindowWriter CreateExecutionResultWriter(SVsServiceProvider serviceProvider)
        {
            return new OutputWindowWriter(serviceProvider,
                Constants.OutputPaneExecutionResultGuid, Constants.OutputPaneExecutionResultTitle);
        }

        public static async Task PrintExecutionResultAsync(this IOutputWindowWriter writer, string tag, ExecutionCompleted result)
        {
            var stdout = result.Stdout.TrimEnd('\r', '\n');
            var stderr = result.Stderr.TrimEnd('\r', '\n');

            if (stdout.Length == 0 && stderr.Length == 0)
            {
                await writer.PrintMessageAsync($"[{tag}] No stdout/stderr captured").ConfigureAwait(false);
            }
            else
            {
                await writer.PrintMessageAsync($"[{tag}] Captured stdout", stdout).ConfigureAwait(false);
                await writer.PrintMessageAsync($"[{tag}] Captured stderr", stderr).ConfigureAwait(false);
            }
        }
    }
}
