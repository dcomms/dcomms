﻿using Dcomms.P2PTP;
using Dcomms.P2PTP.LocalLogic;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Dcomms.SUBT.SUBTP
{
    /// <summary>
    /// information about ConnectedPeerStream state, regularly shared between peers
    /// </summary>
    internal class SubtRemoteStatusPacket
    {
        public readonly float RecentRxBandwidth; // per stream

        public float RecentRxPacketLoss => _recentRxPacketLoss; // per stream // 0..1
        public readonly float _recentRxPacketLoss;  // 0..1
        public readonly float RecentTxBandwidth; // actual transmitted bandwidth // per stream

        public readonly bool IhavePassiveRole; // no own-set TX bandwidth target
        public readonly bool ImHealthyAndReadyFor100kbpsU2uSymbiosis;

        static float LimitPacketLoss(float loss)
        {
            if (loss > 1) loss = 1;
            else if (loss < 0) loss = 0;
            return loss;
        }
        public SubtRemoteStatusPacket(float recentRxBandwidth, float recentRxPacketLoss, float recentTxBandwidth, bool ihavePassiveRole, bool imHealthyAndReadyFor100kbpsU2uSymbiosis)
        {
            RecentRxBandwidth = recentRxBandwidth;
            _recentRxPacketLoss = LimitPacketLoss(recentRxPacketLoss);
            RecentTxBandwidth = recentTxBandwidth;
            IhavePassiveRole = ihavePassiveRole;
            ImHealthyAndReadyFor100kbpsU2uSymbiosis = imHealthyAndReadyFor100kbpsU2uSymbiosis;
        }
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendFormat("rxBw={0};rxLoss={1:0.0}%;txBw={2};passive={3}",
                RecentRxBandwidth.BandwidthToString(), RecentRxPacketLoss * 100, RecentTxBandwidth.BandwidthToString(), IhavePassiveRole);          
            return sb.ToString();
        }

        public SubtRemoteStatusPacket(BinaryReader reader)
        {
            RecentRxBandwidth = reader.ReadSingle();
            _recentRxPacketLoss = LimitPacketLoss(reader.ReadSingle());
            RecentTxBandwidth = reader.ReadSingle();
            var flags = reader.ReadByte();
            IhavePassiveRole = (flags & 0x02) != 0;
            ImHealthyAndReadyFor100kbpsU2uSymbiosis = (flags & 0x04) != 0;           
        }
        public byte[] Encode(SubtConnectedPeerStream connectedStream)
        {
            BinaryProcedures.CreateBinaryWriter(out var ms, out var writer);           
            
            ExtensionProcedures.InitializeExtensionSignalingPacket(writer, connectedStream.SubtLocalPeer.LocalPeer.LocalPeerId, connectedStream.SubtConnectedPeer.RemotePeerId, connectedStream.StreamId, connectedStream.SubtLocalPeer.ExtensionId);
            writer.Write((byte)SubtPacketType.RemoteStatus);
            writer.Write(RecentRxBandwidth);
            writer.Write(RecentRxPacketLoss);
            writer.Write(RecentTxBandwidth);
            byte flags = 0;
            if (IhavePassiveRole) flags |= 0x02;
            if (ImHealthyAndReadyFor100kbpsU2uSymbiosis) flags |= 0x04;
            writer.Write(flags);
            return ms.ToArray();
        }
    }


}
