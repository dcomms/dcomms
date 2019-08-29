using Dcomms.Vision;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace Dcomms.DRP
{
    /// <summary>
    /// error handlers / dev vision / anti-fraud
    /// </summary>
    partial class DrpPeerEngine
    {
        const string VisionChannelModuleName_reg_requesterSide = "reg.requester";
        const string VisionChannelModuleName_reg_responderSide = "reg.responder";
        const string VisionChannelModuleName_reg_epSide = "reg.ep";
        const string VisionChannelModuleName_reg_proxySide = "reg.proxy";
        const string VisionChannelModuleName_engineThread = "engineThread";
        const string VisionChannelModuleName_receiverThread = "receiverThread";
        const string VisionChannelModuleName_p2p = "p2p"; // ping, direct p2p communication
        internal void WriteToLog_p2p_detail(ConnectionToNeighbor connectionToNeighbor, string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.detail, $"[{connectionToNeighbor}] {message}");
        }
        internal void WriteToLog_reg_proxySide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide, AttentionLevel.detail, message);

        }
        void WriteToLog_receiver_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.detail, message);
        }
        void HandleExceptionInReceiverThread(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.strongPain, $"exception: {exc}");
        }
        void HandleExceptionInEngineThread(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_engineThread) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_engineThread, AttentionLevel.strongPain, $"exception: {exc}");
        }
        internal void WriteToLog_reg_requesterSide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.detail, message);
        }
        void WriteToLog_reg_requesterSide_mediumPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.detail, message);

        }
        void HandleExceptionWhileConnectingToRP(IPEndPoint epEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.detail, $"exception while connecting to EP {epEndpoint}: {exc}");

            // todo: analyse if it is malformed packet received from attacker's EP
        }
        internal void HandleGeneralException(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, null) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, null, AttentionLevel.strongPain, $"general exception: {message}");
        }
        internal void OnReceivedUnauthorizedSourceIpPacket(IPEndPoint remoteEndpoint)
        {
        }
        internal void OnReceivedBadRegisterSynPow1(IPEndPoint remoteEndpoint)
        {
        }
        internal void OnReceivedRegisterSynAtoEpPacketFromUnknownSource(IPEndPoint remoteEndpoint)
        { }
        internal void OnReceivedRegisterSynAtoEpPacketWithBadPow2(IPEndPoint remoteEndpoint)
        { }
        void OnReceivedBadSignature(IPEndPoint remoteEndpoint)
        {
        }
        void HandleExceptionInRegistrationResponder(IPEndPoint requesterEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.detail, $"exception while responding to registration from {requesterEndpoint}: {exc}");
        }
        void HandleExceptionWhileProxying(IPEndPoint requesterEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.detail, $"exception while proxying registration from {requesterEndpoint}: {exc}");
        }
        internal void WriteToLog_reg_responderSide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.detail, message);
        }
    }
}
