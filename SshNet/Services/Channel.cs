﻿using SshNet.Messages.Connection;
using System;

namespace SshNet.Services
{
    public abstract class Channel
    {
        protected ConnectionService _connectionService;

        public Channel(ConnectionService connectionService,
            uint clientChannelId, uint clientInitialWindowSize, uint clientMaxPacketSize,
            uint serverChannelId)
        {
            _connectionService = connectionService;

            ClientChannelId = clientChannelId;
            ClientInitialWindowSize = clientInitialWindowSize;
            ClientWindowSize = clientInitialWindowSize;
            ClientMaxPacketSize = clientMaxPacketSize;

            ServerChannelId = serverChannelId;
            ServerInitialWindowSize = Session.InitialLocalWindowSize;
            ServerWindowSize = Session.InitialLocalWindowSize;
            ServerMaxPacketSize = Session.LocalChannelDataPacketSize;
        }

        public uint ClientChannelId { get; private set; }
        public uint ClientInitialWindowSize { get; private set; }
        public uint ClientWindowSize { get; protected set; }
        public uint ClientMaxPacketSize { get; private set; }

        public uint ServerChannelId { get; private set; }
        public uint ServerInitialWindowSize { get; private set; }
        public uint ServerWindowSize { get; protected set; }
        public uint ServerMaxPacketSize { get; private set; }

        public bool ClientClosed { get; private set; }
        public bool ClientMarkedEof { get; private set; }
        public bool ServerClosed { get; private set; }
        public bool ServerMarkedEof { get; private set; }

        public event EventHandler<byte[]> DataReceived;
        public event EventHandler EofReceived;
        public event EventHandler Closing;

        public void SendData(byte[] data)
        {
            var msg = new ChannelDataMessage();
            msg.RecipientChannel = ClientChannelId;

            var total = (uint)data.Length;
            var offset = 0L;
            byte[] buf = null;
            do
            {
                var packetSize = Math.Min(Math.Min(ClientWindowSize, ClientMaxPacketSize), total);
                if (buf == null || packetSize != buf.Length)
                    buf = new byte[packetSize];
                Array.Copy(data, offset, buf, 0, packetSize);

                msg.Data = buf;
                _connectionService._session.SendMessage(msg);

                ClientWindowSize -= packetSize;
                total -= packetSize;
                offset += packetSize;
            } while (total > 0);
        }

        public void SendEof()
        {
            if (ServerMarkedEof)
                return;

            ServerMarkedEof = true;
            var msg = new ChannelEofMessage { RecipientChannel = ClientChannelId };
            _connectionService._session.SendMessage(msg);
        }

        public void SendClose(uint exitCode = 0)
        {
            if (ServerClosed)
                return;

            ServerClosed = true;
            _connectionService._session.SendMessage(new ExitStatusMessage { RecipientChannel = ClientChannelId, ExitStatus = exitCode });
            _connectionService._session.SendMessage(new ChannelCloseMessage { RecipientChannel = ClientChannelId });

            CheckBothClosed();
        }

        internal void OnData(byte[] data)
        {
            ServerAttemptAdjustWindow((uint)data.Length);

            if (DataReceived != null)
                DataReceived(this, data);
        }

        internal void OnEof()
        {
            ClientMarkedEof = true;

            if (EofReceived != null)
                EofReceived(this, EventArgs.Empty);
        }

        internal void OnClose()
        {
            ClientClosed = true;

            if (Closing != null)
                Closing(this, EventArgs.Empty);

            CheckBothClosed();
        }

        internal void ClientAdjustWindow(uint bytesToAdd)
        {
            ClientWindowSize += bytesToAdd;
        }

        private void ServerAttemptAdjustWindow(uint messageLength)
        {
            ServerWindowSize -= messageLength;
            if (ServerWindowSize <= ServerMaxPacketSize)
            {
                _connectionService._session.SendMessage(new ChannelWindowAdjustMessage
                {
                    RecipientChannel = ClientChannelId,
                    BytesToAdd = ServerInitialWindowSize - ServerWindowSize
                });
                ServerWindowSize = ServerInitialWindowSize;
            }
        }

        private void CheckBothClosed()
        {
            if (ClientClosed && ServerClosed)
            {
                _connectionService.RemoveChannel(this);
            }
        }
    }
}
