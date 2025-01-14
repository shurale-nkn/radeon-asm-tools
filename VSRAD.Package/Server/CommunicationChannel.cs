﻿using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Composition;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using VSRAD.DebugServer;
using VSRAD.DebugServer.IPC.Commands;
using VSRAD.DebugServer.IPC.Responses;
using VSRAD.Package.Options;
using VSRAD.Package.ProjectSystem;
using Task = System.Threading.Tasks.Task;

namespace VSRAD.Package.Server
{
    public interface ICommunicationChannel
    {
        Task SendAsync(ICommand command);

        Task<T> SendWithReplyAsync<T>(ICommand command) where T : IResponse;
    }

    public enum ClientState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public interface ICommunicationChannelManager
    {
        event Action ConnectionStateChanged;

        void ForceDisconnect();

        (string connectionInfo, ClientState state) ChannelState { get; }
    }

    [Export(typeof(ICommunicationChannel))]
    [Export(typeof(ICommunicationChannelManager))]
    [AppliesTo(Constants.ProjectCapability)]
    public sealed class CommunicationChannel : ICommunicationChannel, ICommunicationChannelManager
    {
        public event Action ConnectionStateChanged;

        public (string connectionInfo, ClientState state) ChannelState => (ConnectionOptions.ToString(), State);

        private ClientState __state = ClientState.Disconnected;
        private ClientState State
        {
            get => __state;
            set
            {
                __state = value;
                ConnectionStateChanged?.Invoke();
            }
        }

        private ServerConnectionOptions ConnectionOptions => _project.Options.Profile.General.Connection;

        private static readonly TimeSpan _connectionTimeout = new TimeSpan(hours: 0, minutes: 0, seconds: 5);

        private readonly OutputWindowWriter _outputWindowWriter;
        private readonly IProject _project;

        private TcpClient _connection;

        [ImportingConstructor]
        public CommunicationChannel(SVsServiceProvider provider, IProject project)
        {
            _outputWindowWriter = new OutputWindowWriter(provider,
                Constants.OutputPaneServerGuid, Constants.OutputPaneServerTitle);
            _project = project;
            _project.Loaded += (options) =>
                options.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(options.ActiveProfile)) ForceDisconnect(); };
        }

        public async Task SendAsync(ICommand command)
        {
            await EstablishServerConnectionAsync().ConfigureAwait(false);
            try
            {
                await _connection.GetStream().WriteSerializedMessageAsync(command).ConfigureAwait(false);
                await _outputWindowWriter.PrintMessageAsync($"Sent command to {ConnectionOptions}", command.ToString()).ConfigureAwait(false);
            }
            catch (ObjectDisposedException) // ForceDisconnect has been called within the try block 
            {
                throw new OperationCanceledException();
            }
            catch (Exception e)
            {
                ForceDisconnect();
                throw new Exception($"Connection to {ConnectionOptions} has been terminated: {e.Message}");
            }
        }

        public async Task<T> SendWithReplyAsync<T>(ICommand command) where T : IResponse
        {
            await SendAsync(command).ConfigureAwait(false);
            try
            {
                var response = await _connection.GetStream().ReadSerializedMessageAsync<IResponse>().ConfigureAwait(false);
                await _outputWindowWriter.PrintMessageAsync($"Received response from {ConnectionOptions}", response.ToString()).ConfigureAwait(false);
                return (T)response;
            }
            catch (ObjectDisposedException) // ForceDisconnect has been called within the try block 
            {
                throw new OperationCanceledException();
            }
            catch (Exception e)
            {
                ForceDisconnect();
                throw new Exception($"Connection to {ConnectionOptions} has been terminated: {e.Message}");
            }
        }

        public void ForceDisconnect()
        {
            _connection?.Close();
            _connection = null;
            State = ClientState.Disconnected;
        }

        private async Task EstablishServerConnectionAsync()
        {
            if (_connection != null && _connection.Connected) return;

            State = ClientState.Connecting;

            var client = new TcpClient();
            try
            {
                using (var cts = new CancellationTokenSource(_connectionTimeout))
                using (cts.Token.Register(() => client.Dispose()))
                {
                    await client.ConnectAsync(ConnectionOptions.RemoteMachine, ConnectionOptions.Port);
                    _connection = client;
                    State = ClientState.Connected;
                }
            }
            catch (Exception)
            {
                State = ClientState.Disconnected;
                throw new Exception($"Unable to establish connection to a debug server at {ConnectionOptions}");
            }
        }
    }
}
