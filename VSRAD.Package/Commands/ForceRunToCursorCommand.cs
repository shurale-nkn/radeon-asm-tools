﻿using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Threading.Tasks;
using Task = System.Threading.Tasks.Task;

namespace VSRAD.Package.Commands
{
    [ExportCommandGroup(Constants.ForceRunToCursorCommandSet)]
    [AppliesTo(Constants.ProjectCapability)]
    internal sealed class ForceRunToCursorCommand : IAsyncCommandGroupHandler
    {
        private readonly ProjectSystem.DebuggerIntegration _debugger;

        [ImportingConstructor]
        public ForceRunToCursorCommand(ProjectSystem.DebuggerIntegration debugger)
        {
            _debugger = debugger;
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
            _debugger.RunToCurrentLine();

            return true;
        }
    }
}