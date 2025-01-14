﻿using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using VSRAD.Package.ProjectSystem;
using VSRAD.Package.ToolWindows;
using Task = System.Threading.Tasks.Task;

namespace VSRAD.Package.Commands
{
    [ExportCommandGroup(Constants.AddToWatchesCommandSet)]
    [AppliesTo(Constants.ProjectCapability)]
    public sealed class AddToWatchesCommand : IAsyncCommandGroupHandler
    {
        private readonly IToolWindowIntegration _toolIntegration;
        private readonly IActiveCodeEditor _codeEditor;

        [ImportingConstructor]
        public AddToWatchesCommand(IToolWindowIntegration toolIntegration, IActiveCodeEditor codeEditor)
        {
            _toolIntegration = toolIntegration;
            _codeEditor = codeEditor;
        }

        public Task<CommandStatusResult> GetCommandStatusAsync(IImmutableSet<IProjectTree> nodes, long commandId, bool focused, string commandText, CommandStatus progressiveStatus)
        {
            if (commandId == Constants.MenuCommandId)
            {
                return Task.FromResult(new CommandStatusResult(true, commandText, CommandStatus.Enabled | CommandStatus.Supported));
            }
            return Task.FromResult(CommandStatusResult.Unhandled);
        }

        public async Task<bool> TryHandleCommandAsync(IImmutableSet<IProjectTree> nodes, long commandId, bool focused, long commandExecuteOptions, IntPtr variantArgIn, IntPtr variantArgOut)
        {
            if (commandId != Constants.MenuCommandId)
            {
                return false;
            }

            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var activeWord = _codeEditor.GetActiveWord();
            if (!string.IsNullOrWhiteSpace(activeWord))
            {
                var watchName = activeWord.Trim();
                _toolIntegration.AddWatchFromEditor(watchName);
            }

            return true;
        }
    }
}
