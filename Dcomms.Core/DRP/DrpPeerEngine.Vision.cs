using Dcomms.DRP.Packets;
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
        internal const string VisionChannelModuleName_reg_requesterSide = "reg.requester";
        internal const string VisionChannelModuleName_reg_responderSide = "reg.responder";
        internal const string VisionChannelModuleName_reg = "reg";
        const string VisionChannelModuleName_reg_epSide = "reg.ep";
        const string VisionChannelModuleName_reg_proxySide = "reg.proxy";
        internal const string VisionChannelModuleName_inv_requesterSide = "inv.requester";
        internal const string VisionChannelModuleName_inv_proxySide = "inv.proxy";
        internal const string VisionChannelModuleName_inv_responderSide = "inv.responder";
        internal const string VisionChannelModuleName_inv = "inv";
        const string VisionChannelModuleName_dc = "dc";
        const string VisionChannelModuleName_engineThread = "engineThread";
        const string VisionChannelModuleName_receiverThread = "receiverThread";
        internal const string VisionChannelModuleName_p2p = "p2p"; // ping, direct p2p communication
        internal const string VisionChannelModuleName_routing = "routing";
        const string VisionChannelModuleName_udp = "udp";
        internal const string VisionChannelModuleName_attacks = "attacks";
        internal bool WriteToLog_p2p_detail_enabled => Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.detail;
        internal void WriteToLog_p2p_detail(ConnectionToNeighbor connectionToNeighbor, string message, object req)
        {
            if (WriteToLog_p2p_detail_enabled)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.detail,
                    $"[{connectionToNeighbor}] {message}", req, connectionToNeighbor.LocalDrpPeer);
        }
        internal void WriteToLog_p2p_detail2(ConnectionToNeighbor connectionToNeighbor, string message, object req)
        {
             Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.detail,
                    $"[{connectionToNeighbor}] {message}", req, connectionToNeighbor.LocalDrpPeer);
        }

        internal void WriteToLog_p2p_needsAttention(ConnectionToNeighbor connectionToNeighbor, string message, object req)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.needsAttention)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.needsAttention, $"[{connectionToNeighbor}] {message}", req, connectionToNeighbor.LocalDrpPeer);
        }
        internal void WriteToLog_p2p_higherLevelDetail(ConnectionToNeighbor connectionToNeighbor, string message, object req)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.higherLevelDetail)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.higherLevelDetail, $"[{connectionToNeighbor}] {message}", req, connectionToNeighbor.LocalDrpPeer);
        }
        internal void WriteToLog_p2p_lightPain(ConnectionToNeighbor connectionToNeighbor, string message, object req)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.lightPain, $"[{connectionToNeighbor}] {message}", req, connectionToNeighbor.LocalDrpPeer);
        }
        internal void WriteToLog_p2p_mediumPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_p2p, AttentionLevel.mediumPain, message);
        }

        internal bool WriteToLog_udp_detail_enabled => Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp) <= AttentionLevel.detail;
        internal void WriteToLog_udp_detail(string message)
        {
             Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp, AttentionLevel.detail, message);
        }
        internal bool WriteToLog_udp_deepDetail_enabled => Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp) <= AttentionLevel.deepDetail;
        internal void WriteToLog_udp_deepDetail(string message)
        {
            Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp, AttentionLevel.deepDetail, message);
        }
        internal void WriteToLog_udp_lightPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp, AttentionLevel.lightPain, message);
        }
        internal void WriteToLog_udp_needsAttention(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp) <= AttentionLevel.needsAttention)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_udp, AttentionLevel.needsAttention, message);
        }

        bool WriteToLog_receiver_detail_enabled => Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.detail;

        void WriteToLog_receiver_detail(string message)
        {
             Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.detail, message);
        }
        void WriteToLog_receiver_lightPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.lightPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.lightPain, message);
        }
        void HandleExceptionInReceiverThread(Exception exc)
        {
            if (exc is BadSignatureException) { WriteToLog_attacks_strongPain($"exception: {exc}"); return; }

            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_receiverThread, AttentionLevel.strongPain, $"exception: {exc}");
        }
        void HandleExceptionInEngineThread(Exception exc)
        {
            if (exc is BadSignatureException) { WriteToLog_attacks_strongPain($"exception: {exc}"); return; }

            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_engineThread) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_engineThread, AttentionLevel.strongPain, $"exception: {exc}");            
        }
        internal void WriteToLog_reg_requesterSide_needsAttention(string message, object req, IVisiblePeer localPeer)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.needsAttention)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.needsAttention, message, req, localPeer);
        }
        internal void WriteToLog_reg_requesterSide_detail(string message, object req, IVisiblePeer localPeer)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.detail)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.detail, message, req, localPeer);
        }
        internal void WriteToLog_reg_requesterSide_higherLevelDetail(string message, object req, IVisiblePeer localPeer)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.higherLevelDetail)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.higherLevelDetail, message, req, localPeer);
        }
        internal void WriteToLog_reg_requesterSide_mediumPain(string message, object req, IVisiblePeer localPeer)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.mediumPain, message, req, localPeer);
        }
        internal void WriteToLog_reg_requesterSide_lightPain(string message, object req, IVisiblePeer localPeer)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.lightPain)
            {
                Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.lightPain, message, req, localPeer);
            }
        }
        internal void WriteToLog_reg_responderSide_higherLevelDetail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.higherLevelDetail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.higherLevelDetail, message);
        }
        internal void WriteToLog_drpGeneral_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general) <= AttentionLevel.detail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general, AttentionLevel.detail, message);
        }
        internal void WriteToLog_drpGeneral_higherLevelDetail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general) <= AttentionLevel.higherLevelDetail)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general, AttentionLevel.higherLevelDetail, message);
        }
        internal void WriteToLog_drpGeneral_needsAttention(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general) <= AttentionLevel.needsAttention)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_drp_general, AttentionLevel.needsAttention, message);
        }

        internal void WriteToLog_dc_detail(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_dc) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_dc, AttentionLevel.detail, message);

        }
        void HandleExceptionWhileConnectingToEP(IPEndPoint epEndpoint, Exception exc)
        {
            if (exc is BadSignatureException) { WriteToLog_attacks_strongPain($"exception while connection to EP: {exc}"); return; }
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.detail,
                    $"exception while connecting to EP {epEndpoint} (SystemClock={DateTimeNowUtc}UTC make sure that it is valid): {exc}");

            // todo: analyse if it is malformed packet received from attacker's EP

            // todo try to get correct time UTC
            
        }
        internal void HandleGeneralException(string prefix, Exception exc)
        {
            if (exc is BadSignatureException)
            {
                WriteToLog_attacks_strongPain($"exception: {exc}"); 
                return; 
            }
            if (exc is DrpTimeoutException)
            {
                if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, null) <= AttentionLevel.needsAttention)
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, null, AttentionLevel.needsAttention, $"{prefix}: {exc}");
                return;
            }
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, null) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, null, AttentionLevel.strongPain, $"{prefix}: {exc}");
        }

        #region attacks
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

        internal void WriteToLog_attacks_strongPain(string message)
        {
            if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_attacks) <= AttentionLevel.strongPain)
                Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_attacks, AttentionLevel.strongPain, message);
        }
        #endregion

        //void HandleExceptionInRegistrationRequester(Exception exc)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide) <= AttentionLevel.mediumPain)
        //        Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_requesterSide, AttentionLevel.mediumPain, $"exception while sending REGISTER request: {exc}");

        //}
        //void HandleExceptionInRegistrationResponder(RegisterRequestPacket req, IPEndPoint requesterEndpoint, Exception exc)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.mediumPain)
        //        Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.mediumPain, $"exception while responding to REGISTER REQ {req.RequesterRegistrationId} from {requesterEndpoint}: {exc}");
        //}
        //void HandleExceptionWhileProxyingRegister(RegisterRequestPacket req, IVisiblePeer localPeer, IPEndPoint requesterEndpoint, Exception exc)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide) <= AttentionLevel.mediumPain)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_proxySide, AttentionLevel.mediumPain,
        //            $"exception while proxying {req} from {requesterEndpoint}: {exc}", req, localPeer);
        //}
        //internal void WriteToLog_reg_responderSide_detail(string message, object req, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.detail)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.detail, message, req, localPeer);
        //}
        //internal void WriteToLog_reg_responderSide_higherLevelDetail(string message, object req, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.higherLevelDetail)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.higherLevelDetail, message, req, localPeer);
        //}
        //internal void WriteToLog_reg_responderSide_lightPain(string message, object req, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.lightPain)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.lightPain, message, req, localPeer);
        //}
        //internal void WriteToLog_reg_responderSide_needsAttention(string message, object req, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide) <= AttentionLevel.needsAttention)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_reg_responderSide, AttentionLevel.needsAttention, message, req, localPeer);
        //}
        //internal void HandleExceptionInInviteRequester(Exception exc, object req, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide) <= AttentionLevel.mediumPain)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_requesterSide, AttentionLevel.mediumPain, $"exception while sending invite request: {exc}", req, localPeer);
        //}
        //internal void HandleExceptionWhileProxyingInvite(InviteRequestPacket req, Exception exc, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_proxySide) <= AttentionLevel.mediumPain)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_proxySide, AttentionLevel.mediumPain, $"exception while proxying invite from {req.RequesterRegistrationId}: {exc}", req, localPeer);
        //}
        //internal void HandleExceptionWhileAcceptingInvite(Exception exc, object req, IVisiblePeer localPeer)
        //{
        //    if (Configuration.VisionChannel?.GetAttentionTo(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_responderSide) <= AttentionLevel.mediumPain)
        //        Configuration.VisionChannel?.EmitPeerInRoutedPath(Configuration.VisionChannelSourceId, VisionChannelModuleName_inv_responderSide, AttentionLevel.mediumPain, $"exception while accepting invite: {exc}", req, localPeer);
        //}




        DateTime? _lastTimeUtcEmittedHighEngineThreadQueueDelay_LightPain = null;
        DateTime? _lastTimeUtcEmittedHighEngineThreadQueueDelay_MediumPain = null;
        void OnMeasuredEngineThreadQueueDelay(DateTime dtUtc, double delayMs)
        {
            if (delayMs > 300)
            {
                if (_lastTimeUtcEmittedHighEngineThreadQueueDelay_LightPain == null || (dtUtc - _lastTimeUtcEmittedHighEngineThreadQueueDelay_LightPain.Value).TotalMilliseconds > 5000)
                {
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_engineThread, AttentionLevel.lightPain, $"high delay in engine thread queue: {delayMs}ms. {ETSC.PeakExecutionTimeStats}");
                    _lastTimeUtcEmittedHighEngineThreadQueueDelay_LightPain = dtUtc;
                }
            }
            if (delayMs > 2000)
            {
                if (_lastTimeUtcEmittedHighEngineThreadQueueDelay_MediumPain == null || (dtUtc - _lastTimeUtcEmittedHighEngineThreadQueueDelay_MediumPain.Value).TotalMilliseconds > 5000)
                {
                    Configuration.VisionChannel?.Emit(Configuration.VisionChannelSourceId, VisionChannelModuleName_engineThread, AttentionLevel.mediumPain, $"high delay in engine thread queue: {delayMs}ms. {ETSC.PeakExecutionTimeStats}");
                    _lastTimeUtcEmittedHighEngineThreadQueueDelay_MediumPain = dtUtc;
                }
            }
        }
    }



    public class Logger
    {
        readonly IVisiblePeer _localPeer;
        readonly object _req;
        public string ModuleName;
        readonly VisionChannel _visionChannel;
        readonly string _visionChannelSourceId;
        public readonly DrpPeerEngine Engine;
        public Logger(DrpPeerEngine engine, IVisiblePeer localPeer, object req, string moduleName)
        {
            Engine = engine;
            _visionChannel = engine.Configuration.VisionChannel;
            _visionChannelSourceId = engine.Configuration.VisionChannelSourceId;
            _localPeer = localPeer;
            _req = req;
            ModuleName = moduleName;
        }

        internal void WriteToLog_higherLevelDetail(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.higherLevelDetail)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, ModuleName, AttentionLevel.higherLevelDetail,
                    message, _req, _localPeer);
        }
        internal bool WriteToLog_detail_enabled => _visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.detail;
        internal void WriteToLog_detail(string message)
        {
            _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, ModuleName, AttentionLevel.detail,
                    message, _req, _localPeer);
        }
        internal bool WriteToLog_deepDetail_enabled => _visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.deepDetail;
        internal void WriteToLog_deepDetail(string message)
        {
            _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, ModuleName, AttentionLevel.deepDetail,
                    message, _req, _localPeer);
        }
        internal void WriteToLog_needsAttention(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.needsAttention)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, ModuleName, AttentionLevel.needsAttention,
                    message, _req, _localPeer);
        }
        internal void WriteToLog_lightPain(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.lightPain)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, ModuleName, AttentionLevel.lightPain, message, _req, _localPeer);
        }
        internal void WriteToLog_lightPain_EmitListOfPeers(string message, IVisiblePeer selectedPeer = null)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.lightPain)
                _visionChannel.EmitListOfPeers(_visionChannelSourceId, ModuleName, AttentionLevel.lightPain, message, null, selectedPeer);
        }
        internal void WriteToLog_higherLevelDetail_EmitListOfPeers(string message, IVisiblePeer selectedPeer = null)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.higherLevelDetail)
                _visionChannel.EmitListOfPeers(_visionChannelSourceId, ModuleName, AttentionLevel.higherLevelDetail, message, null, selectedPeer);
        }
        internal void WriteToLog_needsAttention_EmitListOfPeers(string message, IVisiblePeer selectedPeer = null)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.needsAttention)
                _visionChannel.EmitListOfPeers(_visionChannelSourceId, ModuleName, AttentionLevel.needsAttention, message, null, selectedPeer);
        }
        internal void WriteToLog_mediumPain(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.mediumPain)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, ModuleName, AttentionLevel.mediumPain,
                    message, _req, _localPeer);
        }
        internal void WriteToLog_mediumPain_EmitListOfPeers(string message, IVisiblePeer selectedPeer = null)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, ModuleName) <= AttentionLevel.mediumPain)
                _visionChannel.EmitListOfPeers(_visionChannelSourceId, ModuleName, AttentionLevel.mediumPain,
                    message, null, selectedPeer);
        }

        internal void WriteToLog_attacks(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_attacks) <= AttentionLevel.strongPain)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_attacks, AttentionLevel.strongPain,
                    message, _req, _localPeer);
        }
        internal void WriteToLog_routing_detail(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_routing) <= AttentionLevel.detail)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_routing, AttentionLevel.detail, message, _req, _localPeer);
        }
        internal void WriteToLog_routing_higherLevelDetail(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_routing) <= AttentionLevel.higherLevelDetail)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_routing, AttentionLevel.higherLevelDetail,  message, _req, _localPeer);
        }
        internal void WriteToLog_routing_needsAttention(string message)
        {
            if (_visionChannel?.GetAttentionTo(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_routing) <= AttentionLevel.needsAttention)
                _visionChannel.EmitPeerInRoutedPath(_visionChannelSourceId, DrpPeerEngine.VisionChannelModuleName_routing, AttentionLevel.needsAttention,
                    message, _req, _localPeer);
        }
    }

}
