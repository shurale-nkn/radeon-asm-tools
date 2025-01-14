﻿using System.Windows;
using System.Windows.Controls;
using VSRAD.Package.ToolWindows;
using VSRAD.Package.Utils;

namespace VSRAD.Package.DebugVisualizer
{
    public sealed partial class VisualizerControl : UserControl
    {
        private readonly IToolWindowIntegration _integration;
        private readonly VisualizerTable _table;

        private Server.BreakState _breakState;

        public VisualizerControl(IToolWindowIntegration integration)
        {
            InitializeComponent();
            _integration = integration;
            headerControl.Setup(integration,
                getGroupCount: (groupSize) => _breakState?.GetGroupCount(groupSize) ?? 0,
                GroupSelectionChanged);
            headerControl.GroupSizeChanged += ApplyColumnStyling;
            Application.Current.Deactivated += (sender, e) =>
            {
                _table.HostWindowDeactivated();
            };

            integration.BreakEntered += BreakEntered;
            integration.AddWatch += AddWatch;
            integration.ProjectOptions.VisualizerOptions.PropertyChanged += VisualizerOptionsChanged;
            integration.ProjectOptions.VisualizerColumnStyling.StylingChanged += ApplyColumnStyling;

            _table = new VisualizerTable(
                _integration.ProjectOptions.VisualizerColumnStyling,
                groupSizeGetter: () => (int)headerControl.GroupSize);
            _table.WatchStateChanged += (newWatchState, invalidatedRows) =>
            {
                _integration.ProjectOptions.DebuggerOptions.Watches = newWatchState;
                if (invalidatedRows != null)
                    foreach (var row in invalidatedRows)
                        SetRowContentsFromBreakState(row);
            };
            tableHost.Setup(_table);
            RestoreSavedState();
        }

        private void RestoreSavedState()
        {
            ApplyColumnStyling();
            _table.Rows.Clear();
            _table.AppendVariableRow(new Watch("System", VariableType.Hex, isAVGPR: false), canBeRemoved: false);
            _table.ShowSystemRow = _integration.ProjectOptions.VisualizerOptions.ShowSystemVariable;
            foreach (var watch in _integration.ProjectOptions.DebuggerOptions.Watches)
                _table.AppendVariableRow(watch);
            _table.PrepareNewWatchRow();
        }

        private void VisualizerOptionsChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(Options.VisualizerOptions.ShowSystemVariable):
                    _table.ShowSystemRow = _integration.ProjectOptions.VisualizerOptions.ShowSystemVariable;
                    break;
                case nameof(Options.VisualizerOptions.MaskLanes):
                case nameof(Options.VisualizerOptions.LaneGrouping):
                case nameof(Options.VisualizerOptions.CheckMagicNumber):
                    ApplyColumnStyling();
                    break;
                case nameof(Options.VisualizerOptions.MagicNumber):
                    if (_integration.ProjectOptions.VisualizerOptions.CheckMagicNumber)
                        ApplyColumnStyling();
                    break;
            }
        }

        public void WindowFocusLost()
        {
            _table.HostWindowDeactivated();
        }

        public void ApplyColumnStyling()
        {
            var scrollingOffset = _table.HorizontalScrollingOffset;
            _table.SuspendDrawing(); // prevents the scrollbar from jerking due to visibility changes

            _integration.ProjectOptions.VisualizerColumnStyling.Computed.Apply(_table.DataColumns,
                groupSize: headerControl.GroupSize,
                laneGrouping: _integration.ProjectOptions.VisualizerOptions.LaneGrouping);

            if (_breakState != null && _integration.ProjectOptions.VisualizerOptions.MaskLanes)
                ColumnStyling.ApplyLaneMask(_table.DataColumns,
                    groupSize: headerControl.GroupSize, system: _breakState.System);

            if (_breakState != null && _integration.ProjectOptions.VisualizerOptions.CheckMagicNumber)
                ColumnStyling.ApplyMagicNumber(_table.DataColumns,
                    groupSize: headerControl.GroupSize,
                    system: _breakState.System,
                    magicNumber: _integration.ProjectOptions.VisualizerOptions.MagicNumber);

            _table.ResumeDrawing();
            _table.HorizontalScrollingOffset = scrollingOffset;
        }

        public void BreakEntered(Server.BreakState breakState)
        {
            Ensure.ArgumentNotNull(breakState, nameof(breakState));
            _breakState = breakState;
            _breakState.GroupSize = headerControl.GroupSize;
            headerControl.OnDataAvailable();
        }

        public void AddWatch(string watchName)
        {
            _table.RemoveNewWatchRow();
            _table.AppendVariableRow(new Watch(watchName, VariableType.Hex, isAVGPR: false));
            _table.PrepareNewWatchRow();
            _integration.ProjectOptions.DebuggerOptions.Watches = _table.GetCurrentWatchState();
        }

        private void GroupSelectionChanged(uint groupIndex, string coordinates)
        {
            headerControl.OnPendingDataRequest(coordinates);
            VSPackage.TaskFactory.RunAsync(async () =>
            {
                var result = await _breakState.ChangeGroupAsync(groupIndex, headerControl.GroupSize);
                await VSPackage.TaskFactory.SwitchToMainThreadAsync();
                if (result.TryGetResult(out _, out var error))
                {
                    foreach (System.Windows.Forms.DataGridViewRow row in _table.Rows)
                        SetRowContentsFromBreakState(row);
                }
                else
                {
                    Errors.Show(error);
                    foreach (System.Windows.Forms.DataGridViewRow row in _table.Rows)
                        EraseRowData(row);
                }

                ApplyColumnStyling();
                RowStyling.ResetRowStyling(_table.DataRows);
                RowStyling.GreyOutUnevaluatedWatches(_breakState.Watches, _table.DataRows);
                headerControl.OnDataRequestCompleted(_breakState.GetGroupCount(headerControl.GroupSize), _breakState.TotalElapsedMilliseconds, _breakState.ExecElapsedMilliseconds);
            });
        }

        private void SetRowContentsFromBreakState(System.Windows.Forms.DataGridViewRow row)
        {
            if (_breakState == null) return;
            if (row.Index == 0) // system
            {
                RenderRowData(row, _breakState.System);
            }
            else
            {
                var watch = (string)row.Cells[VisualizerTable.NameColumnIndex].Value;
                if (_breakState.TryGetWatch(watch, out var values))
                    RenderRowData(row, values);
                else
                    EraseRowData(row);
            }
        }

        private void EraseRowData(System.Windows.Forms.DataGridViewRow row)
        {
            for (int i = 0; i < VisualizerTable.DataColumnCount; ++i)
                row.Cells[i + VisualizerTable.DataColumnOffset].Value = "";
        }

        private void RenderRowData(System.Windows.Forms.DataGridViewRow row, uint[] data)
        {
            var variableType = VariableTypeUtils.TypeFromShortName(row.HeaderCell.Value.ToString());
            for (int i = 0; i < headerControl.GroupSize; i++)
                row.Cells[i + VisualizerTable.DataColumnOffset].Value = DataFormatter.FormatDword(variableType, data[i]);
        }
    }
}
