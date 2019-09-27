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
        const string VisionChannelModuleName_drp_general = "drp.general";
        const string VisionChannelModuleName_reg_requesterSide = "reg.requester";
        const string VisionChannelModuleName_reg_responderSide = "reg.responder";
        const string VisionChannelModuleName_reg_epSide = "reg.ep";
        const string VisionChannelModuleName_reg_proxySide = "reg.proxy";
        const string VisionChannelModuleName_inv_requesterSide = "inv.requester";
        const string VisionChannelModuleName_inv_proxySide = "inv.proxy";
        const string VisionChannelModuleName_inv_responderSide = "inv.responder";
        const string VisionChannelModuleName_dc = "dc";
        const string VisionChannelModuleName_engineThread = "engineThread";
        const string VisionChannelModuleName_receiverThread = "receiverThread";
        const string VisionChannelModuleName_p2p = "p2p"; // ping, direct p2p communication
        const string VisionChannelModuleName_routing = "routing";
        const string VisionChannelModuleName_udp = "udp";
        internal void WriteToLog_p2p_detail(ConnectionToNeighbor connectionToNeighbor, string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.detail, $"[{connectionToNeighbor}] {message}");
        }
        internal void WriteToLog_p2p_lightPain(ConnectionToNeighbor connectionToNeighbor, string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.lightPain, $"[{connectionToNeighbor}] {message}");
        }
        internal void WriteToLog_routing_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_routing) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_routing, AttentionLevel.detail, message);
        }
        internal void WriteToLog_udp_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp, AttentionLevel.detail, message);
        }
        internal void WriteToLog_udp_lightPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp, AttentionLevel.lightPain, message);
        }
        internal void WriteToLog_reg_proxySide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide, AttentionLevel.detail, message);
        }
        internal void WriteToLog_reg_proxySide_lightPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide, AttentionLevel.lightPain, message);
        }
        void WriteToLog_receiver_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.detail, message);
        }
        void WriteToLog_receiver_lightPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.lightPain, message);
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
        internal void WriteToLog_drpGeneral_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general, AttentionLevel.detail, message);
        }
        internal void WriteToLog_reg_requesterSide_mediumPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.detail, message);

        }
        internal void WriteToLog_inv_requesterSide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide, AttentionLevel.detail, message);
        }
        internal void WriteToLog_inv_requesterSide_higherLevelDetail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide) <= AttentionLevel.higherLevelDetail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide, AttentionLevel.higherLevelDetail, message);
        }
        internal void WriteToLog_inv_proxySide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_proxySide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_proxySide, AttentionLevel.detail, message);

        }
        internal void WriteToLog_inv_responderSide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_responderSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_responderSide, AttentionLevel.detail, message);

        }
        internal void WriteToLog_dc_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_dc) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_dc, AttentionLevel.detail, message);

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
        internal void OnReceivedBadRegisterReqPow1(IPEndPoint remoteEndpoint)
        {
        }
        internal void OnReceivedRegisterReqAtoEpPacketFromUnknownSource(IPEndPoint remoteEndpoint)
        { }
        internal void OnReceivedRegisterReqAtoEpPacketWithBadPow2(IPEndPoint remoteEndpoint)
        { }
        void OnReceivedBadSignature(IPEndPoint remoteEndpoint)
        {
        }
        void HandleExceptionInRegistrationRequester(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.mediumPain, $"exception while sending REGISTER request: {exc}");

        }
        void HandleExceptionInRegistrationResponder(IPEndPoint requesterEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.mediumPain, $"exception while responding to registration from {requesterEndpoint}: {exc}");
        }
        void HandleExceptionWhileProxyingRegister(IPEndPoint requesterEndpoint, Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide, AttentionLevel.mediumPain, $"exception while proxying registration from {requesterEndpoint}: {exc}");
        }
        internal void WriteToLog_reg_responderSide_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.detail, message);
        }
        internal void WriteToLog_reg_responderSide_lightPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.lightPain, message);
        }
        internal void HandleExceptionInInviteRequester(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide, AttentionLevel.mediumPain, $"exception while sending invite request: {exc}");
        }
        internal void HandleExceptionWhileProxyingInvite(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_proxySide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_proxySide, AttentionLevel.mediumPain, $"exception while proxying invite: {exc}");
        }
        internal void HandleExceptionWhileAcceptingInvite(Exception exc)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_responderSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_responderSide, AttentionLevel.mediumPain, $"exception while accepting invite: {exc}");
        }
    }
}
